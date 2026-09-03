using System.Text;

namespace PowerX.Core.Diagnostics.Crash;

/// <summary>One parsed Windows Error Reporting <c>Report.wer</c> file.</summary>
public sealed record WerReport
{
    public required string EventType { get; init; }        // APPCRASH, AppHang, BEX, CLR20r3, MoAppCrash, …
    public required DateTimeOffset When { get; init; }
    public string? FriendlyName { get; init; }
    public string? AppName { get; init; }
    public string? AppVersion { get; init; }
    public string? AppPath { get; init; }
    public string? FaultModule { get; init; }
    public string? FaultModuleVersion { get; init; }
    public string? ExceptionCode { get; init; }            // "c0000005"
    public string? HangType { get; init; }
    public string? ManagedExceptionType { get; init; }     // CLR20r3 P09
    public string ReportFolder { get; init; } = "";
    public string? MinidumpPath { get; init; }
    public IReadOnlyDictionary<string, string> Signatures { get; init; } =
        new Dictionary<string, string>();
}

/// <summary>
/// Reads the per-event folders WER leaves under <c>ReportArchive</c> / <c>ReportQueue</c>.
/// The current user's store needs no elevation; the machine store (ProgramData) does.
/// Pure file reading — nothing is written, nothing is submitted.
/// </summary>
public static class WerReportReader
{
    private static IEnumerable<string> StoreRoots(bool includeMachine)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Path.Combine(local, @"Microsoft\Windows\WER\ReportArchive");
        yield return Path.Combine(local, @"Microsoft\Windows\WER\ReportQueue");
        if (includeMachine)
        {
            string pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            yield return Path.Combine(pd, @"Microsoft\Windows\WER\ReportArchive");
            yield return Path.Combine(pd, @"Microsoft\Windows\WER\ReportQueue");
        }
    }

    public static IReadOnlyList<WerReport> Read(DateTimeOffset since, bool includeMachine = false, int max = 200)
    {
        var list = new List<WerReport>();
        foreach (var root in StoreRoots(includeMachine))
        {
            if (!Directory.Exists(root)) continue;
            IEnumerable<string> folders;
            try { folders = Directory.EnumerateDirectories(root); }
            catch (Exception) { continue; }   // ACL on the machine store when not elevated

            foreach (var folder in folders)
            {
                if (list.Count >= max) return list;
                try
                {
                    var wer = Path.Combine(folder, "Report.wer");
                    if (!File.Exists(wer)) continue;
                    var info = new FileInfo(wer);
                    if (info.LastWriteTimeUtc < since.UtcDateTime) continue;
                    if (Parse(wer, folder, info.LastWriteTime) is { } r) list.Add(r);
                }
                catch (Exception) { /* skip an unreadable / mid-write folder */ }
            }
        }
        return list.OrderByDescending(r => r.When).ToList();
    }

    internal static WerReport? Parse(string werPath, string folder, DateTime fallbackTime)
    {
        // Report.wer is UTF-16 with a BOM; fall back to UTF-8 for odd cases.
        string[] lines;
        try { lines = File.ReadAllLines(werPath, Encoding.Unicode); }
        catch (Exception) { return null; }
        if (lines.Length == 1 && lines[0].Contains('\0'))       // mis-decoded
            lines = File.ReadAllLines(werPath, Encoding.UTF8);

        var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sigNames = new Dictionary<int, string>();
        var sigValues = new Dictionary<int, string>();

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = line[..eq].Trim();
            string val = line[(eq + 1)..].Trim();

            if (TryIndexed(key, "Sig[", "].Name", out int sn)) { sigNames[sn] = val; continue; }
            if (TryIndexed(key, "Sig[", "].Value", out int sv)) { sigValues[sv] = val; continue; }
            kv[key] = val;
        }

        // Zip the Sig name/value pairs into a friendly dictionary.
        var sig = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (idx, name) in sigNames)
            if (!string.IsNullOrEmpty(name) && sigValues.TryGetValue(idx, out var v))
                sig[name] = v;

        string eventType = kv.GetValueOrDefault("EventType", "").Trim();
        if (eventType.Length == 0) eventType = kv.GetValueOrDefault("ConsentKey", "Unknown");

        // CLR unhandled exception: signatures P01..P09.
        bool clr = eventType.StartsWith("CLR20", StringComparison.OrdinalIgnoreCase)
                   || sig.ContainsKey("Problem Signature 09");

        string? Get(params string[] keys)
        {
            foreach (var k in keys)
                if (sig.TryGetValue(k, out var v) && v.Length > 0 && v != "unknown") return v;
            return null;
        }

        var when = ParseWhen(kv) ?? new DateTimeOffset(fallbackTime);

        string? dump = Directory.EnumerateFiles(folder, "*.*")
            .FirstOrDefault(f => f.EndsWith(".mdmp", StringComparison.OrdinalIgnoreCase)
                              || f.EndsWith(".dmp", StringComparison.OrdinalIgnoreCase));

        return new WerReport
        {
            EventType = eventType,
            When = when,
            FriendlyName = kv.GetValueOrDefault("FriendlyEventName"),
            AppName = Get("Application Name", "Problem Signature 01") ?? kv.GetValueOrDefault("AppName"),
            AppVersion = Get("Application Version", "Problem Signature 02"),
            AppPath = kv.GetValueOrDefault("AppPath"),
            FaultModule = clr ? Get("Problem Signature 04") : Get("Fault Module Name"),
            FaultModuleVersion = clr ? Get("Problem Signature 05") : Get("Fault Module Version"),
            ExceptionCode = Get("Exception Code"),
            HangType = Get("Hang Type"),
            ManagedExceptionType = clr ? Get("Problem Signature 09") : null,
            ReportFolder = folder,
            MinidumpPath = dump,
            Signatures = sig,
        };
    }

    private static DateTimeOffset? ParseWhen(Dictionary<string, string> kv)
    {
        // Some reports carry "Sig[..]" timestamps; the reliable field is the report creation
        // time in the folder name (…_<epoch>_…) or "ReportIdentifier". Fall back to file time.
        if (kv.TryGetValue("EventTime", out var et) && long.TryParse(et, out var ft) && ft > 0)
        {
            try { return DateTimeOffset.FromFileTime(ft); } catch (Exception) { /* out of range */ }
        }
        return null;
    }

    private static bool TryIndexed(string key, string prefix, string suffix, out int index)
    {
        index = 0;
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        if (!key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return false;
        var mid = key[prefix.Length..^suffix.Length];
        return int.TryParse(mid, out index);
    }
}
