using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;

namespace PowerX.Core.Diagnostics;

public enum EventLevel2 { Critical, Error, Warning }

public sealed record EventGroup
{
    public required string Log { get; init; }
    public required string Provider { get; init; }
    public required int EventId { get; init; }
    public required EventLevel2 Level { get; init; }
    public required int Count { get; init; }
    public required DateTimeOffset FirstSeen { get; init; }
    public required DateTimeOffset LastSeen { get; init; }
    public string SampleMessage { get; init; } = "";

    /// <summary>A plain-language note for a well-known id, or null.</summary>
    public string? Explanation { get; init; }
}

/// <summary>
/// A friendlier read of the Windows event logs than Event Viewer: recent errors and warnings from
/// Application, System and Setup, grouped by source and id with a count and a plain-language note
/// for the common ones. Read-only. Needs administrator rights for the System log.
/// </summary>
[SupportedOSPlatform("windows")]
public static class EventLogBrowser
{
    private static readonly string[] Logs = ["Application", "System", "Setup"];

    public static async Task<IReadOnlyList<EventGroup>> ReadAsync(
        TimeSpan window, bool includeWarnings, CancellationToken ct = default)
        => await Task.Run(() => Read(window, includeWarnings), ct);

    public static IReadOnlyList<EventGroup> Read(TimeSpan window, bool includeWarnings)
    {
        var since = DateTimeOffset.UtcNow - window;
        string iso = since.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        string levelClause = includeWarnings ? "(Level=1 or Level=2 or Level=3)" : "(Level=1 or Level=2)";

        var groups = new Dictionary<(string log, string provider, int id),
            (int n, DateTimeOffset first, DateTimeOffset last, EventLevel2 lvl, string msg)>();

        foreach (var log in Logs)
        {
            try
            {
                var q = new EventLogQuery(log, PathType.LogName,
                    $"*[System[{levelClause} and TimeCreated[@SystemTime>='{iso}']]]")
                { ReverseDirection = true };
                using var reader = new EventLogReader(q);

                int seen = 0;
                for (EventRecord? e = SafeRead(reader); e is not null && seen < 6000; e = SafeRead(reader), seen++)
                {
                    using (e)
                    {
                        var key = (log, e.ProviderName ?? "(unknown)", e.Id);
                        var when = e.TimeCreated is { } tc
                            ? new DateTimeOffset(tc.ToUniversalTime(), TimeSpan.Zero) : since;
                        var lvl = (e.Level ?? 4) switch { 1 => EventLevel2.Critical, 2 => EventLevel2.Error, _ => EventLevel2.Warning };

                        if (groups.TryGetValue(key, out var cur))
                        {
                            groups[key] = (cur.n + 1,
                                when < cur.first ? when : cur.first,
                                when > cur.last ? when : cur.last,
                                cur.lvl, cur.msg);
                        }
                        else
                        {
                            string msg = "";
                            try { msg = (e.FormatDescription() ?? "").Split('\n')[0].Trim(); } catch { }
                            groups[key] = (1, when, when, lvl, msg);
                        }
                    }
                }
            }
            catch (Exception) { /* log not readable (usually elevation) */ }
        }

        return groups
            .Select(kv => new EventGroup
            {
                Log = kv.Key.log,
                Provider = kv.Key.provider,
                EventId = kv.Key.id,
                Level = kv.Value.lvl,
                Count = kv.Value.n,
                FirstSeen = kv.Value.first,
                LastSeen = kv.Value.last,
                SampleMessage = kv.Value.msg,
                Explanation = Explain(kv.Key.provider, kv.Key.id),
            })
            .OrderByDescending(g => g.Level == EventLevel2.Critical)
            .ThenByDescending(g => g.Count)
            .ToList();
    }

    private static EventRecord? SafeRead(EventLogReader reader)
    {
        try { return reader.ReadEvent(); }
        catch (Exception) { return null; }
    }

    /// <summary>Plain-language notes for the event ids people actually see and worry about.</summary>
    private static string? Explain(string provider, int id) => (provider, id) switch
    {
        ("Microsoft-Windows-Kernel-Power", 41) =>
            "The PC restarted without shutting down cleanly (power loss, a hard lock, or a hold of the power button). If it repeats, suspect the PSU, overheating, RAM, or a driver.",
        ("Microsoft-Windows-WER-SystemErrorReporting", 1001) =>
            "A bug check (blue screen). The Crash insights page decodes the stop code.",
        ("EventLog", 6008) => "The previous shutdown was unexpected.",
        ("Microsoft-Windows-DistributedCOM", 10016) =>
            "A DCOM permission warning. Almost always harmless and safe to ignore; Microsoft has said as much.",
        ("Microsoft-Windows-DistributedCOM", 10010) =>
            "A COM server did not register in time. Usually harmless unless a specific feature is broken.",
        ("Application Error", 1000) => "A desktop program crashed. The Crash insights page has the details.",
        ("Application Hang", 1002) => "A program stopped responding and was closed.",
        (".NET Runtime", 1026) => "An unhandled exception in a .NET program.",
        ("Microsoft-Windows-Kernel-EventTracing", 1) or ("Microsoft-Windows-Kernel-EventTracing", 2) =>
            "An event-tracing session could not start. Common, low impact, often caused by leftover sessions from monitoring tools.",
        ("Service Control Manager", 7000) or ("Service Control Manager", 7001) or ("Service Control Manager", 7009) or ("Service Control Manager", 7011) =>
            "A service failed to start or timed out. If it names a driver or a feature you use, worth investigating; many are optional services that fail quietly.",
        ("Microsoft-Windows-DNS-Client", 1014) => "A DNS name resolution timed out. Network or DNS server hiccup.",
        ("disk", 7) or ("disk", 11) or ("disk", 51) =>
            "A disk I/O error or bad block. If it repeats, back up now and check the drive's SMART health on the Tools page.",
        ("Microsoft-Windows-Ntfs", 55) or ("Ntfs", 55) =>
            "File-system corruption was detected on a volume. Run chkdsk from the Repair page.",
        ("Microsoft-Windows-DriverFrameworks-UserMode", 10111) =>
            "A user-mode driver (often a USB device) restarted. Usually a device being unplugged awkwardly.",
        ("Microsoft-Windows-Time-Service", 134) => "The time service could not reach a time source. Clock may drift.",
        ("Microsoft-Windows-WindowsUpdateClient", 20) or ("Microsoft-Windows-WindowsUpdateClient", 25) =>
            "A Windows Update failed to install. The Repair page can reset the Update components.",
        ("Microsoft-Windows-GroupPolicy", 1085) or ("Microsoft-Windows-GroupPolicy", 1129) =>
            "A Group Policy extension failed to apply. On a home PC this is usually a transient network issue at logon.",
        ("Microsoft-Windows-Perflib", 1008) or ("Microsoft-Windows-Perflib", 1010) =>
            "A performance-counter provider failed to load. Cosmetic unless you use that counter.",
        _ => null,
    };
}
