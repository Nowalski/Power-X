using System.Diagnostics;
using Microsoft.UI.Xaml;

namespace PowerX.App;

public partial class App : Application
{
    public static MainWindow? Window { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => { Log("Task", e.Exception); e.SetObserved(); };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        ApplyAccent();
        Window = new MainWindow();
        Window.Activate();
    }

    /// <summary>
    /// Override the six accent-tint resources before any brush resolves them, so a custom
    /// accent flows through every derived brush. "System" leaves Windows' own accent alone.
    /// </summary>
    internal static void ApplyAccent()
    {
        try
        {
            var hex = Services.AppSettings.Current.Accent;
            if (string.IsNullOrWhiteSpace(hex) || hex.Equals("System", StringComparison.OrdinalIgnoreCase))
                return;
            if (!TryParseHex(hex, out var baseColor)) return;

            var res = Current.Resources;
            res["SystemAccentColor"] = baseColor;
            res["SystemAccentColorLight1"] = Mix(baseColor, 255, 0.20);
            res["SystemAccentColorLight2"] = Mix(baseColor, 255, 0.40);
            res["SystemAccentColorLight3"] = Mix(baseColor, 255, 0.60);
            res["SystemAccentColorDark1"] = Mix(baseColor, 0, 0.20);
            res["SystemAccentColorDark2"] = Mix(baseColor, 0, 0.40);
            res["SystemAccentColorDark3"] = Mix(baseColor, 0, 0.60);
        }
        catch (Exception ex) { Log("ApplyAccent", ex); }
    }

    private static bool TryParseHex(string hex, out Windows.UI.Color color)
    {
        color = default;
        hex = hex.TrimStart('#');
        if (hex.Length != 6 || !uint.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var v))
            return false;
        color = Windows.UI.Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        return true;
    }

    private static Windows.UI.Color Mix(Windows.UI.Color c, byte toward, double amount) => Windows.UI.Color.FromArgb(
        255,
        (byte)(c.R + (toward - c.R) * amount),
        (byte)(c.G + (toward - c.G) * amount),
        (byte)(c.B + (toward - c.B) * amount));

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        // e.Message carries the XAML line/detail that e.Exception.ToString() omits.
        Log("UI", new Exception($"{e.Message}\n--- exception ---\n{e.Exception}", e.Exception));
        // keep the app alive so a single bad page can't take down the whole tool
        e.Handled = true;
    }

    internal static void Log(string source, Exception? ex)
    {
        if (ex is null) return;
        try
        {
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerX");
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "crash.log"),
                $"[{DateTimeOffset.Now:o}] {source}: {ex}\n\n");
        }
        catch (Exception logEx)
        {
            Debug.WriteLine(logEx);
        }
    }
}
