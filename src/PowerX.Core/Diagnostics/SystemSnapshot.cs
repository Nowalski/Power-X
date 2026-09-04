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
        var items = new Dictionary<SnapshotCategory, List<SnapshotItem>>();

        items[SnapshotCategory.Startup] = Safe(() => StartupProvider.Enumerate()
            .Where(e => e.Source is not (StartupSource.RunOnceUser or StartupSource.RunOnceMachine))
            .Select(e => new SnapshotItem(
                $"{e.Source}:{e.Name}".ToLowerInvariant(),
                e.Name,
                e.Enabled ? "enabled" : "disabled"))
            .ToList());

        items[SnapshotCategory.ScheduledTask] = Safe(() => ScheduledTasks.Enumerate()
            .Select(t => new SnapshotItem(t.Path.ToLowerInvariant(), t.Name, t.Enabled ? "enabled" : "disabled"))
            .ToList());

        items[SnapshotCategory.Service] = Safe(() => ServiceProvider.Enumerate()
            .Where(s => s.StartMode is ServiceStartMode2.Automatic or ServiceStartMode2.AutomaticDelayed or ServiceStartMode2.Disabled)
            .Select(s => new SnapshotItem(s.Name.ToLowerInvariant(), s.DisplayName, s.StartModeText))
            .ToList());

        items[SnapshotCategory.Program] = Safe(() => InstalledPrograms.Enumerate()
            .Select(p => new SnapshotItem(
                (p.Name + "|" + p.Scope).ToLowerInvariant(),
                p.Name,
                string.IsNullOrWhiteSpace(p.Version) ? "installed" : p.Version))
            .ToList());

        items[SnapshotCategory.Driver] = Safe(ReadDrivers);

        items[SnapshotCategory.Tweak] = Safe(() =>
        {
            var engine = new TweakEngine(TweakCatalog.Default);
            return engine.GetAllStatus()
                .Where(s => s.State == TweakState.Applied)
                .Select(s => new SnapshotItem(s.Definition.Id, s.Definition.Name, "applied"))
                .ToList();
        });

        string build = "";
        try { build = Registry_ReadBuild(); } catch { }

        return new ConfigSnapshot
        {
            TakenAt = DateTimeOffset.Now,
            WindowsBuild = build,
            Automatic = automatic,
            Items = items,
        };
    }

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

    /// <summary>Take a snapshot now if the newest one is older than <paramref name="maxAge"/>.</summary>
    public static ConfigSnapshot? CaptureIfStale(TimeSpan maxAge)
    {
        var newest = List().FirstOrDefault();
        if (newest.Path is not null && DateTimeOffset.Now - newest.When < maxAge) return null;
        var snap = Capture(automatic: true);
        Save(snap);
        return snap;
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

        foreach (SnapshotCategory cat in Enum.GetValues<SnapshotCategory>())
        {
            var before = (from.Items.GetValueOrDefault(cat) ?? []).ToDictionary(i => i.Key);
            var after = (to.Items.GetValueOrDefault(cat) ?? []).ToDictionary(i => i.Key);

            foreach (var (key, item) in after)
            {
                if (!before.TryGetValue(key, out var old))
                    changes.Add(new SnapshotChange(cat, item.Label, ChangeKind.Added, null, item.State));
                else if (!string.Equals(old.State, item.State, StringComparison.OrdinalIgnoreCase))
                    changes.Add(new SnapshotChange(cat, item.Label, ChangeKind.Changed, old.State, item.State));
            }
            foreach (var (key, old) in before)
                if (!after.ContainsKey(key))
                    changes.Add(new SnapshotChange(cat, old.Label, ChangeKind.Removed, old.State, null));
        }

        changes = changes
            .OrderBy(c => c.Category)
            .ThenBy(c => c.Kind)
            .ThenBy(c => c.Label, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new SnapshotDiff(from.TakenAt, to.TakenAt, changes);
    }
}
