using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;

namespace PowerX.Core.Diagnostics.Crash;

public enum EventCrashKind { AppError, AppHang, WerReference, DotNetRuntime, Bugcheck, UnexpectedShutdown }

/// <summary>One relevant entry from the Application or System event log.</summary>
public sealed record EventCrashRecord
{
    public required EventCrashKind Kind { get; init; }
    public required DateTimeOffset When { get; init; }
    public string? App { get; init; }
    public string? AppVersion { get; init; }
    public string? FaultModule { get; init; }
    public string? FaultModulePath { get; init; }
    public string? ExceptionCode { get; init; }        // "0xc0000005"
    public string? HangType { get; init; }
    public string? ManagedExceptionType { get; init; }
    public IReadOnlyList<string> ManagedStackTop { get; init; } = [];
    public int? BugcheckCode { get; init; }
    public string? BugcheckParams { get; init; }
    public string? DumpPath { get; init; }
    public long RecordId { get; init; }
}

/// <summary>
/// Reads the crash-relevant event-log entries. The Application log is readable by a normal user;
/// the System log needs no elevation to read either. Read-only; a query failure yields an empty
/// list, never an exception across the boundary.
/// </summary>
[SupportedOSPlatform("windows")]
public static class EventLogCrashReader
{
    public static IReadOnlyList<EventCrashRecord> Read(DateTimeOffset since, int max = 300)
    {
        var result = new List<EventCrashRecord>();
        string iso = since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        Query(result,
            "Application",
            $"*[System[(EventID=1000 or EventID=1001 or EventID=1002 or EventID=1026) and TimeCreated[@SystemTime>='{iso}']]]",
            max);

        Query(result,
            "System",
            "*[System[((Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and EventID=1001) " +
            $"or (Provider[@Name='EventLog'] and (EventID=6008))) and TimeCreated[@SystemTime>='{iso}']]]",
            max);

        return result.OrderByDescending(r => r.When).ToList();
    }

    private static void Query(List<EventCrashRecord> into, string log, string xpath, int max)
    {
        EventLogReader? reader = null;
        try
        {
            reader = new EventLogReader(new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true });
        }
        catch (Exception)
        {
            return;   // channel missing / access denied / malformed query on this OS
        }

        try
        {
            for (EventRecord? e = reader.ReadEvent(); e is not null && into.Count < max; e = reader.ReadEvent())
            {
                using (e)
                {
                    try
                    {
                        if (Convert(e) is { } rec) into.Add(rec);
                    }
                    catch (Exception) { /* skip a single malformed event */ }
                }
            }
        }
        catch (Exception) { /* enumeration failed midway — keep what we have */ }
        finally { reader.Dispose(); }
    }

    private static EventCrashRecord? Convert(EventRecord e)
    {
        var when = new DateTimeOffset(e.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow, TimeSpan.Zero);
        var data = e.Properties.Select(p => p.Value?.ToString() ?? "").ToList();
        string P(int i) => i >= 0 && i < data.Count ? data[i] : "";

        switch (e.Id)
        {
            case 1000: // Application Error
                return new EventCrashRecord
                {
                    Kind = EventCrashKind.AppError,
                    When = when,
                    App = NullIf(P(0)),
                    AppVersion = NullIf(P(1)),
                    FaultModule = NullIf(P(3)),
                    ExceptionCode = Hex(P(6)),
                    FaultModulePath = NullIf(P(11)),
                    RecordId = e.RecordId ?? 0,
                };

            case 1002: // Application Hang
                return new EventCrashRecord
                {
                    Kind = EventCrashKind.AppHang,
                    When = when,
                    App = NullIf(P(0)),
                    AppVersion = NullIf(P(1)),
                    HangType = NullIf(P(4)),
                    RecordId = e.RecordId ?? 0,
                };

            case 1001: // WER — Application log = reference; System log = bugcheck
                if (string.Equals(e.LogName, "System", StringComparison.OrdinalIgnoreCase) ||
                    (e.ProviderName?.Contains("SystemErrorReporting", StringComparison.OrdinalIgnoreCase) ?? false))
                {
                    var (code, prm, dump) = ParseBugcheck(SafeMessage(e), data);
                    return new EventCrashRecord
                    {
                        Kind = EventCrashKind.Bugcheck, When = when,
                        BugcheckCode = code, BugcheckParams = prm, DumpPath = dump,
                        RecordId = e.RecordId ?? 0,
                    };
                }
                return new EventCrashRecord
                {
                    Kind = EventCrashKind.WerReference, When = when,
                    App = NullIf(P(1)),                       // often the app / bucket name
                    DumpPath = data.FirstOrDefault(d => d.EndsWith(".mdmp", StringComparison.OrdinalIgnoreCase)
                                                     || d.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase)),
                    RecordId = e.RecordId ?? 0,
                };

            case 6008: // "The previous system shutdown at <time> was unexpected."
                return new EventCrashRecord
                {
                    Kind = EventCrashKind.UnexpectedShutdown, When = when, RecordId = e.RecordId ?? 0,
                };

            case 1026: // .NET Runtime — one big text blob
                var (type, frames, app) = ParseDotNet(SafeMessage(e), P(0));
                return new EventCrashRecord
                {
                    Kind = EventCrashKind.DotNetRuntime, When = when,
                    App = app, ManagedExceptionType = type, ManagedStackTop = frames,
                    RecordId = e.RecordId ?? 0,
                };

            default:
                return null;
        }
    }

    private static string SafeMessage(EventRecord e)
    {
        try { return e.FormatDescription() ?? ""; } catch (Exception) { return ""; }
    }

    private static (int? code, string? prm, string? dump) ParseBugcheck(string msg, List<string> data)
    {
        // "The bugcheck was: 0x0000001e (0xffff..., 0x0, 0x0, 0x0). A dump was saved in: C:\Windows\MEMORY.DMP."
        int? code = null; string? prm = null; string? dump = null;

        var m = System.Text.RegularExpressions.Regex.Match(msg,
            @"0x([0-9a-fA-F]{8})\s*\(([^)]*)\)");
        if (m.Success)
        {
            if (int.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out var c))
                code = c;
            prm = m.Groups[2].Value.Trim();
        }
        var d = System.Text.RegularExpressions.Regex.Match(msg, @"saved in:?\s*(?<p>[A-Za-z]:\\[^\r\n.]+\.[Dd][Mm][Pp])");
        if (d.Success) dump = d.Groups["p"].Value.Trim();

        // Fall back to raw data properties (data[0] = code string, data[1] = dump path).
        if (code is null && data.Count > 0)
        {
            var mm = System.Text.RegularExpressions.Regex.Match(data[0], @"0x([0-9a-fA-F]{8})");
            if (mm.Success && int.TryParse(mm.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out var c2))
                code = c2;
        }
        dump ??= data.FirstOrDefault(x => x.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));
        return (code, prm, dump);
    }

    private static (string? type, IReadOnlyList<string> frames, string? app) ParseDotNet(string msg, string firstProp)
    {
        string text = msg.Length > 0 ? msg : firstProp;
        string? app = null, type = null;
        var frames = new List<string>();

        foreach (var line in text.Split('\n'))
        {
            var t = line.Trim();
            if (t.StartsWith("Application:", StringComparison.OrdinalIgnoreCase))
                app = t["Application:".Length..].Trim();
            else if (t.StartsWith("Exception Info:", StringComparison.OrdinalIgnoreCase))
            {
                var e = t["Exception Info:".Length..].Trim();
                int colon = e.IndexOf(':');
                type ??= colon > 0 ? e[..colon].Trim() : e;
            }
            else if ((t.StartsWith("at ", StringComparison.Ordinal) || t.StartsWith("--->", StringComparison.Ordinal))
                     && frames.Count < 6)
                frames.Add(t);
        }
        return (type, frames, app);
    }

    private static string? NullIf(string s) => string.IsNullOrWhiteSpace(s) || s == "unknown" ? null : s;

    private static string? Hex(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return null;
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return s.ToLowerInvariant();
        return long.TryParse(s, out var _) ? $"0x{long.Parse(s):x}" : $"0x{s}";
    }
}
