using System.Management;
using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerX.Core.Programs;
using PowerX.Core.Services;
using PowerX.Core.Startup;
using PowerX.Core.Tweaks;

namespace PowerX.Core.Diagnostics;

public enum SnapshotCategory { Startup, ScheduledTask, Service, Program, Driver, Tweak }

/// <summary>One thing that was present when a snapshot was taken. <see cref="Key"/> identifies it
/// across snapshots; <see cref="State"/> is what a diff compares (version, start mode, on/off).</summary>
public sealed record SnapshotItem(string Key, string Label, string State);

public sealed record ConfigSnapshot
{
    public required DateTimeOffset TakenAt { get; init; }
    public string WindowsBuild { get; init; } = "";
    public bool Automatic { get; init; }
    public required Dictionary<SnapshotCategory, List<SnapshotItem>> Items { get; init; }

    /// <summary>
    /// The startup sources (<see cref="StartupSource"/> names) this snapshot read. Null in
    /// snapshots saved before this was recorded; those covered
    /// <see cref="SystemSnapshot.LegacyStartupSources"/>. Stored as strings so a snapshot written by
    /// a newer PowerX, naming a source this one does not know, still loads.
    /// </summary>
    public List<string>? StartupSources { get; init; }
}

public enum ChangeKind { Added, Removed, Changed }

public sealed record SnapshotChange(SnapshotCategory Category, string Label, ChangeKind Kind, string? Before, string? After);

public sealed record SnapshotDiff(
    DateTimeOffset FromWhen, DateTimeOffset ToWhen, IReadOnlyList<SnapshotChange> Changes)
{
    public bool Any => Changes.Count > 0;
}

/// <summary>
/// Periodic, read-only snapshots of the machine's configuration — startup entries, scheduled
/// tasks, auto-start services, installed programs, signed drivers and applied PowerX tweaks —
/// so PowerX can answer "what changed since last week?". Snapshots are plain JSON under
/// <c>%LOCALAPPDATA%\PowerX\snapshots</c>; nothing leaves the machine.
/// </summary>
[SupportedOSPlatform("windows")]
public static class SystemSnapshot
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerX", "snapshots");

    private const int Keep = 40;

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    // ---------------------------------------------------------------- capture

    public static ConfigSnapshot Capture(bool automatic = false)
    {
        // The six categories read six unrelated sources (registry, Task Scheduler COM, the service
        // database, the uninstall keys, a WMI driver query, and the tweak engine) and share no
        // state, so they are collected concurrently and assembled afterwards rather than one after
        // another. The driver query alone is about a second, which used to be added to, rather
        // than overlapped with, everything else: measured on the dev machine, the whole capture
        // went from ~1950ms to ~1250ms, which is now just the driver query it cannot go below.
        // Each still runs inside Safe(), so one failing source leaves the others intact.
        var startup = Task.Run(() => Safe(CaptureStartup));
        var tasks = Task.Run(() => Safe(CaptureScheduledTasks));
        var services = Task.Run(() => Safe(CaptureServices));
        var programs = Task.Run(() => Safe(CapturePrograms));
        var drivers = Task.Run(() => Safe(ReadDrivers));
        var tweaks = Task.Run(() => Safe(CaptureTweaks));
        Task.WaitAll(startup, tasks, services, programs, drivers, tweaks);

        var items = new Dictionary<SnapshotCategory, List<SnapshotItem>>
        {
            [SnapshotCategory.Startup] = startup.Result,
            [SnapshotCategory.ScheduledTask] = tasks.Result,
            [SnapshotCategory.Service] = services.Result,
            [SnapshotCategory.Program] = programs.Result,
            [SnapshotCategory.Driver] = drivers.Result,
            [SnapshotCategory.Tweak] = tweaks.Result,
        };

        string build = "";
        try { build = Registry_ReadBuild(); } catch { }

        return new ConfigSnapshot
        {
            TakenAt = DateTimeOffset.Now,
            WindowsBuild = build,
            Automatic = automatic,
            Items = items,
            StartupSources = SnapshotStartupSources.Select(s => s.ToString()).ToList(),
        };
    }

    /// <summary>
    /// The startup sources a snapshot records. RunOnce entries are left out because they delete
    /// themselves after running, which a diff would report as a change nobody made; scheduled
    /// tasks have their own category.
    /// </summary>
    internal static readonly StartupSource[] SnapshotStartupSources =
    [
        StartupSource.RunUser, StartupSource.RunMachine, StartupSource.RunMachine32,
        StartupSource.StartupFolderUser, StartupSource.StartupFolderCommon,
    ];

    /// <summary>
    /// What every snapshot covered before <see cref="ConfigSnapshot.StartupSources"/> was recorded,
    /// which was also before PowerX read the 32-bit Run key.
    /// </summary>
    internal static readonly StartupSource[] LegacyStartupSources =
    [
        StartupSource.RunUser, StartupSource.RunMachine,
        StartupSource.StartupFolderUser, StartupSource.StartupFolderCommon,
    ];

    private static List<SnapshotItem> CaptureStartup() => StartupProvider.Enumerate()
        .Where(e => SnapshotStartupSources.Contains(e.Source))
        .Select(StartupItem)
        .ToList();

    /// <summary>
    /// The key is "source:name", so a 32-bit entry and a 64-bit entry with the same name stay two
    /// separate items, and the key for every source that existed before keeps its old spelling,
    /// so older snapshots still line up with new ones.
    /// </summary>
    internal static SnapshotItem StartupItem(StartupEntry e) => new(
        $"{e.Source}:{e.Name}".ToLowerInvariant(),
        e.Name,
        e.Enabled ? "enabled" : "disabled");

    private static List<SnapshotItem> CaptureScheduledTasks() => ScheduledTasks.Enumerate()
        .Select(t => new SnapshotItem(t.Path.ToLowerInvariant(), t.Name, t.Enabled ? "enabled" : "disabled"))
        .ToList();

    private static List<SnapshotItem> CaptureServices() => ServiceProvider.Enumerate()
        .Where(s => s.StartMode is ServiceStartMode2.Automatic or ServiceStartMode2.AutomaticDelayed or ServiceStartMode2.Disabled)
        .Select(s => new SnapshotItem(s.Name.ToLowerInvariant(), s.DisplayName, s.StartModeText))
        .ToList();

    private static List<SnapshotItem> CapturePrograms() => InstalledPrograms.Enumerate()
        .Select(p => new SnapshotItem(
            (p.Name + "|" + p.Scope).ToLowerInvariant(),
            p.Name,
            string.IsNullOrWhiteSpace(p.Version) ? "installed" : p.Version))
        .ToList();

    private static List<SnapshotItem> CaptureTweaks() =>
        new TweakEngine(TweakCatalog.Default).GetAllStatus()
            .Where(s => s.State == TweakState.Applied)
            .Select(s => new SnapshotItem(s.Definition.Id, s.Definition.Name, "applied"))
            .ToList();

    private static List<SnapshotItem> ReadDrivers()
    {
        var list = new List<SnapshotItem>();
        using var searcher = new ManagementObjectSearcher(
            "SELECT DeviceName, DriverVersion, DriverProviderName, DriverDate FROM Win32_PnPSignedDriver WHERE DeviceName IS NOT NULL");
        foreach (ManagementBaseObject o in searcher.Get())
        {
            string? name = o["DeviceName"]?.ToString();
            string? ver = o["DriverVersion"]?.ToString();
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(ver)) continue;
            string? provider = o["DriverProviderName"]?.ToString();
            list.Add(new SnapshotItem(
                name.ToLowerInvariant(),
                provider is { Length: > 0 } and not "Microsoft" ? $"{name} ({provider})" : name,
                ver));
        }
        // de-dup by key, keep highest version string
        return list.GroupBy(i => i.Key)
                   .Select(g => g.OrderByDescending(i => i.State, StringComparer.OrdinalIgnoreCase).First())
                   .ToList();
    }

    private static string Registry_ReadBuild()
    {
        using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
            @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        return k?.GetValue("CurrentBuildNumber")?.ToString() ?? "";
    }

    private static List<SnapshotItem> Safe(Func<List<SnapshotItem>> read)
    {
        try { return read(); } catch (Exception) { return []; }
    }

    // ---------------------------------------------------------------- storage

    public static void Save(ConfigSnapshot snapshot)
    {
        Directory.CreateDirectory(Dir);
        string file = Path.Combine(Dir, $"snapshot-{snapshot.TakenAt:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(snapshot, Json));
        Prune();
    }

    public static IReadOnlyList<(DateTimeOffset When, string Path)> List()
    {
        if (!Directory.Exists(Dir)) return [];
        var list = new List<(DateTimeOffset, string)>();
        foreach (var f in Directory.EnumerateFiles(Dir, "snapshot-*.json"))
        {
            var name = Path.GetFileNameWithoutExtension(f)["snapshot-".Length..];
            if (DateTimeOffset.TryParseExact(name, "yyyyMMdd-HHmmss", null,
                System.Globalization.DateTimeStyles.AssumeLocal, out var when))
                list.Add((when, f));
        }
        return list.OrderByDescending(x => x.Item1).ToList();
    }

    public static ConfigSnapshot? Load(string path)
    {
        try { return JsonSerializer.Deserialize<ConfigSnapshot>(File.ReadAllText(path), Json); }
        catch (Exception) { return null; }
    }

    public static ConfigSnapshot? LoadLatest() => List() is [var (_, path), ..] ? Load(path) : null;

    private static readonly Lock StaleGate = new();

    /// <summary>Take a snapshot now if the newest one is older than <paramref name="maxAge"/>.
    /// Safe to call from several places at once on start-up; only the first wins.</summary>
    public static ConfigSnapshot? CaptureIfStale(TimeSpan maxAge)
    {
        lock (StaleGate)
        {
            var newest = List().FirstOrDefault();
            if (newest.Path is not null && DateTimeOffset.Now - newest.When < maxAge) return null;
            var snap = Capture(automatic: true);
            Save(snap);
            return snap;
        }
    }

    private static void Prune()
    {
        var all = List();
        foreach (var (_, path) in all.Skip(Keep))
            try { File.Delete(path); } catch { }
    }

    // ---------------------------------------------------------------- diff

    public static SnapshotDiff Diff(ConfigSnapshot from, ConfigSnapshot to)
    {
        var changes = new List<SnapshotChange>();

        // A startup entry from a source only one of the two snapshots looked at is not a change
        // on the PC. Without this, the first diff after PowerX started reading the 32-bit Run key
        // listed every existing 32-bit entry as newly added.
        var fromSources = CoveredStartupSources(from);
        var toSources = CoveredStartupSources(to);

        foreach (SnapshotCategory cat in Enum.GetValues<SnapshotCategory>())
        {
            // Two entries can legitimately share a key (an installer that registers the same
            // display name twice), so collapse rather than let ToDictionary throw.
            var before = ByKey(from.Items.GetValueOrDefault(cat));
            var after = ByKey(to.Items.GetValueOrDefault(cat));

            foreach (var (key, item) in after)
            {
                if (!before.TryGetValue(key, out var old))
                {
                    if (cat == SnapshotCategory.Startup && !Covers(fromSources, key)) continue;
                    changes.Add(new SnapshotChange(cat, item.Label, ChangeKind.Added, null, item.State));
                }
                else if (!string.Equals(old.State, item.State, StringComparison.OrdinalIgnoreCase))
                    changes.Add(new SnapshotChange(cat, item.Label, ChangeKind.Changed, old.State, item.State));
            }
            foreach (var (key, old) in before)
            {
                if (after.ContainsKey(key)) continue;
                if (cat == SnapshotCategory.Startup && !Covers(toSources, key)) continue;
                changes.Add(new SnapshotChange(cat, old.Label, ChangeKind.Removed, old.State, null));
            }
        }

        changes = changes
            .OrderBy(c => c.Category)
            .ThenBy(c => c.Kind)
            .ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SnapshotDiff(from.TakenAt, to.TakenAt, changes);
    }

    private static Dictionary<string, SnapshotItem> ByKey(List<SnapshotItem>? items)
    {
        var d = new Dictionary<string, SnapshotItem>();
        foreach (var i in items ?? []) d[i.Key] = i;   // last write wins
        return d;
    }

    private static HashSet<string> CoveredStartupSources(ConfigSnapshot s) =>
        new(s.StartupSources ?? LegacyStartupSources.Select(x => x.ToString()), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether a snapshot that read <paramref name="sources"/> would have seen the startup item
    /// with this key. Keys are "source:name". A key whose prefix is not a startup source name is
    /// counted as covered: there is nothing to say it was out of view, so it is reported as before.
    /// </summary>
    private static bool Covers(HashSet<string> sources, string key)
    {
        int colon = key.IndexOf(':');
        if (colon <= 0) return true;
        string source = key[..colon];
        bool isKnownSource = Enum.GetNames<StartupSource>().Contains(source, StringComparer.OrdinalIgnoreCase);
        return !isKnownSource || sources.Contains(source);
    }
}
