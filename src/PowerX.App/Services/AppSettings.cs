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
    public int SamplingMs { get; set; } = 1000;
    public int ReorderSeconds { get; set; } = 3;
    public string ProcessColumnWidths { get; set; } = "";
    public bool ConfirmProcessActions { get; set; } = true;
    public bool BackgroundThrottle { get; set; } = true;
    public bool AutoCheckUpdates { get; set; } = true;
    public DateTimeOffset LastUpdateCheck { get; set; } = DateTimeOffset.MinValue;
    public string DismissedUpdateVersion { get; set; } = "";

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
