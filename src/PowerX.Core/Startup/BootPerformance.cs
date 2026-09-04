using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using System.Xml.Linq;

namespace PowerX.Core.Startup;

public enum StartupImpact { NotMeasured, Low, Medium, High }
public enum BootItemKind { App, Service, Driver }

/// <summary>One thing Windows flagged as slowing a recent start-up.</summary>
public sealed record BootItem
{
    public required string Name { get; init; }
    public string? Path { get; init; }
    public required BootItemKind Kind { get; init; }
    public required int TotalMs { get; init; }        // how long the item took
    public required int DegradationMs { get; init; }  // how much of that was slower than usual
    public required DateTimeOffset When { get; init; }

    public StartupImpact Impact => DegradationMs switch
    {
        >= 1000 => StartupImpact.High,
        >= 300 => StartupImpact.Medium,
        > 0 => StartupImpact.Low,
        _ => StartupImpact.NotMeasured,
    };
}

public sealed record BootTimeline
{
    public required DateTimeOffset LastBootWhen { get; init; }
    public required int LastBootMs { get; init; }
    public required int MainPathMs { get; init; }        // the part you actually wait for
    public int AverageBootMs { get; init; }              // mean of the recent boots
    public int StartupAppCount { get; init; }
    public bool Degraded { get; init; }                  // this boot was flagged slower than usual
}

/// <summary>
/// Reads the boot-performance data Windows records in
/// <c>Microsoft-Windows-Diagnostics-Performance/Operational</c> — the same source as Task
/// Manager's "Startup impact". That log needs administrator rights to read; without them this
/// returns an empty result rather than throwing. Read-only.
/// </summary>
[SupportedOSPlatform("windows")]
public static class BootPerformance
{
    private const string Log = "Microsoft-Windows-Diagnostics-Performance/Operational";
    private static readonly XNamespace Ns = "http://schemas.microsoft.com/win/2004/08/events/event";

    public static (BootTimeline? Timeline, IReadOnlyList<BootItem> Items) Read(int recentBoots = 12)
    {
        try
        {
            return Parse(ReadEvents(), recentBoots);
        }
        catch (Exception)
        {
            return (null, []);   // log missing, access denied, or unreadable on this OS
        }
    }

    private static IEnumerable<(int Id, string Xml, DateTimeOffset When)> ReadEvents()
    {
        var q = new EventLogQuery(Log, PathType.LogName,
            "*[System[(EventID=100 or EventID=101 or EventID=102 or EventID=103)]]")
        { ReverseDirection = true };
        using var reader = new EventLogReader(q);
        for (EventRecord? e = SafeRead(reader); e is not null; e = SafeRead(reader))
        {
            using (e)
            {
                string xml;
                try { xml = e.ToXml(); }
                catch { continue; }
                var when = new DateTimeOffset(e.TimeCreated?.ToUniversalTime() ?? DateTime.UtcNow, TimeSpan.Zero);
                yield return (e.Id, xml, when);
            }
        }
    }

    internal static (BootTimeline? Timeline, IReadOnlyList<BootItem> Items) Parse(
        IEnumerable<(int Id, string Xml, DateTimeOffset When)> events, int recentBoots)
    {
        var boots = new List<(DateTimeOffset when, int total, int main, int apps, bool degraded)>();
        var items = new List<BootItem>();
        int bootsSeen = 0;

        foreach (var (id, xml, when) in events)
        {
            if (bootsSeen > recentBoots || items.Count >= 400) break;

            XElement? data;
            try { data = XElement.Parse(xml).Element(Ns + "EventData"); }
            catch { continue; }
            if (data is null) continue;

            string D(string name) => data.Elements(Ns + "Data")
                .FirstOrDefault(x => (string?)x.Attribute("Name") == name)?.Value ?? "";
            int V(string name) => int.TryParse(D(name), out var v) ? v : 0;

            switch (id)
            {
                case 100:
                    bootsSeen++;
                    boots.Add((when, V("BootTime"), V("MainPathBootTime"), V("BootNumStartupApps"),
                               D("BootIsDegradation") is "true" or "1"));
                    break;
                case 101 when bootsSeen <= recentBoots:
                    AddItem(items, D("Name"), D("Path"), BootItemKind.App, V("TotalTime"), V("DegradationTime"), when);
                    break;
                case 102 when bootsSeen <= recentBoots:
                    AddItem(items, D("Name"), D("Path"), BootItemKind.Driver, V("TotalTime"), V("DegradationTime"), when);
                    break;
                case 103 when bootsSeen <= recentBoots:
                    AddItem(items, D("Name"), null, BootItemKind.Service, V("Duration"), V("DegradationTime"), when);
                    break;
            }
        }

        var deduped = items
            .GroupBy(i => (i.Name.ToLowerInvariant(), i.Kind))
            .Select(g => g.OrderByDescending(i => i.When).First())
            .OrderByDescending(i => i.DegradationMs)
            .ToList();

        if (boots.Count == 0) return (null, deduped);

        var latest = boots[0];   // ReverseDirection => newest first
        var withTotals = boots.Where(b => b.total > 0).Select(b => b.total).ToList();
        return (new BootTimeline
        {
            LastBootWhen = latest.when,
            LastBootMs = latest.total,
            MainPathMs = latest.main,
            AverageBootMs = withTotals.Count > 0 ? (int)withTotals.Average() : 0,
            StartupAppCount = latest.apps,
            Degraded = latest.degraded,
        }, deduped);
    }

    private static void AddItem(List<BootItem> into, string name, string? path, BootItemKind kind, int total, int degradation, DateTimeOffset when)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        into.Add(new BootItem
        {
            Name = name.Trim(), Path = string.IsNullOrWhiteSpace(path) ? null : path.Trim(),
            Kind = kind, TotalMs = total, DegradationMs = degradation, When = when,
        });
    }

    private static EventRecord? SafeRead(EventLogReader reader)
    {
        try { return reader.ReadEvent(); }
        catch (Exception) { return null; }
    }
}
