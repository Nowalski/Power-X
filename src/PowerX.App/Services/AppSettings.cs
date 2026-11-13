using System.Text.Json;

namespace PowerX.App.Services;

/// <summary>Per-user app preferences, persisted to %LOCALAPPDATA%\PowerX\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly string Path = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerX", "settings.json");

    public string Theme { get; set; } = "System";           // System | Light | Dark
    public string Backdrop { get; set; } = "Mica";          // Mica | Acrylic | None
    public string Accent { get; set; } = "System";          // System | #RRGGBB
    /// <summary>How often the live dashboards sample. Two seconds by default: a tick costs about
    /// 10 ms of work, so this keeps PowerX to roughly half a percent of one core while idling in
    /// the background, and the charts still move often enough to watch. Settings offers 0.5 s
    /// through 5 s for anyone who wants it livelier or cheaper.</summary>
    public int SamplingMs { get; set; } = 2000;
    public int ReorderSeconds { get; set; } = 3;
    public string ProcessColumnWidths { get; set; } = "";
    public bool ConfirmProcessActions { get; set; } = true;
    public bool BackgroundThrottle { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;
    /// <summary>The page PowerX opens on: a navigation tag ("home", "health", "processes"...), or
    /// "last" for <see cref="LastPage"/>.</summary>
    public string StartPage { get; set; } = "home";
    /// <summary>The page that was showing when PowerX last closed.</summary>
    public string LastPage { get; set; } = "home";
    /// <summary>When an update check last succeeded. A failed check does not move this, so the
    /// daily silent check retries next launch instead of waiting another 20 hours.</summary>
    public DateTimeOffset LastUpdateCheck { get; set; } = DateTimeOffset.MinValue;
    /// <summary>When an update check last failed, and why. Settings shows it when it is newer
    /// than <see cref="LastUpdateCheck"/>.</summary>
    public DateTimeOffset LastUpdateFailure { get; set; } = DateTimeOffset.MinValue;
    public string LastUpdateError { get; set; } = "";
    public string DismissedUpdateVersion { get; set; } = "";

    // Main window placement in physical pixels (AppWindow coordinates), as it was when PowerX
    // last closed. Width 0 = never saved. X/Y/Width/Height are the restored (not maximized)
    // bounds, so un-maximizing after a restart returns to the size the user chose.
    public int WindowX { get; set; }
    public int WindowY { get; set; }
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public static AppSettings Current { get; private set; } = Load();

    private static AppSettings Load()
    {
        foreach (var p in new[] { Path, Path + ".bak" })
        {
            try
            {
                if (File.Exists(p) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(p)) is { } s)
                    return s;
            }
            catch (Exception) { /* corrupt / unreadable — try the backup, then defaults */ }
        }
        return new AppSettings();
    }

    private static readonly Lock SaveGate = new();

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
            string json = JsonSerializer.Serialize(this, JsonOpts);
            lock (SaveGate)
            {
                // Write to a temp file and swap it in, so a crash (or the installer killing us)
                // mid-write can't leave settings.json truncated. Keep the previous good copy as .bak.
                string tmp = Path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(Path)) File.Replace(tmp, Path, Path + ".bak", ignoreMetadataErrors: true);
                else File.Move(tmp, Path);
            }
            Current = this;
        }
        catch (Exception) { /* best effort */ }
    }

    public static string LogFolder => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerX");
}
