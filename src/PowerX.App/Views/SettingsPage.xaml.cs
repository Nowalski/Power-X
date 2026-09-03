using System.Diagnostics;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerX.App.Services;
using PowerX.Core.Diagnostics;
using PowerX.Core.Transactions;
using PowerX.Core.Tweaks;

namespace PowerX.App.Views;

public sealed partial class SettingsPage : Page
{
    private bool _loaded;

    public SettingsPage()
    {
        InitializeComponent();

        PageLayout.CenterCap(this, Root, 900);

        var s = AppSettings.Current;
        ThemeBox.SelectedIndex = s.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
        BackdropBox.SelectedIndex = s.Backdrop switch { "Acrylic" => 1, "None" => 2, _ => 0 };
        BuildAccentSwatches(s.Accent);
        IntervalBox.SelectedIndex = s.SamplingMs switch { 500 => 0, 2000 => 2, 5000 => 3, _ => 1 };
        ThrottleSwitch.IsOn = s.BackgroundThrottle;
        ConfirmSwitch.IsOn = s.ConfirmProcessActions;
        AutoUpdateSwitch.IsOn = s.AutoCheckUpdates;
        UpdateCheckStatus.Text = s.LastUpdateCheck == DateTimeOffset.MinValue
            ? "Never checked"
            : $"Last checked {s.LastUpdateCheck.LocalDateTime:g}";

        var info = SystemInfoProvider.Collect();
        var ver = typeof(SettingsPage).Assembly.GetName().Version;
        AboutText.Text = $"PowerX {ver?.ToString(3)}  ·  {(info.IsElevated ? "running as administrator" : "not elevated, some features are off")}";
        AboutRuntime.Text = $"{info.WindowsEdition} build {info.BuildString} · {info.Architecture} · .NET {Environment.Version}";
        _loaded = true;
    }

    private void OpenHistory_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e) => App.Window?.Navigate("history");

    private void OpenNotices_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "THIRD-PARTY-NOTICES.md");
            if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            else Process.Start(new ProcessStartInfo("https://github.com/Nowalski/Power-X/blob/main/THIRD-PARTY-NOTICES.md") { UseShellExecute = true });
        }
        catch (Exception ex) { App.Log("OpenNotices", ex); }
    }

    private void Theme_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        var s = AppSettings.Current;
        s.Theme = ThemeBox.SelectedIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
        s.Save();
        App.Window?.ApplyTheme();
    }

    private void Backdrop_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || (BackdropBox.SelectedItem as ComboBoxItem)?.Tag is not string tag) return;
        AppSettings.Current.Backdrop = tag;
        AppSettings.Current.Save();
        App.Window?.ApplyBackdrop();
    }

    private static readonly (string Name, string Hex)[] AccentPresets =
    [
        ("System", "System"),
        ("Violet", "6A4CF5"),
        ("Blue", "2563EB"),
        ("Teal", "0D9488"),
        ("Green", "16A34A"),
        ("Amber", "D97706"),
        ("Rose", "E11D48"),
        ("Slate", "475569"),
    ];

    private void BuildAccentSwatches(string current)
    {
        AccentSwatches.Children.Clear();
        foreach (var (name, hex) in AccentPresets)
        {
            bool selected = string.Equals(current, hex, StringComparison.OrdinalIgnoreCase)
                            || (hex == "System" && (string.IsNullOrWhiteSpace(current) || current.Equals("System", StringComparison.OrdinalIgnoreCase)));

            Brush fill = hex == "System"
                ? (Brush)Application.Current.Resources["ControlFillColorDefaultBrush"]
                : new SolidColorBrush(Hex(hex));

            var dot = new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(13),
                Background = fill,
                BorderThickness = new Thickness(selected ? 2 : 1),
                BorderBrush = (Brush)Application.Current.Resources[selected ? "AccentTextFillColorPrimaryBrush" : "CardStrokeColorDefaultBrush"],
            };
            if (hex == "System")
                dot.Child = new TextBlock { Text = "A", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            var btn = new Button
            {
                Padding = new Thickness(3),
                Background = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(0),
                Content = dot,
                Tag = hex,
            };
            ToolTipService.SetToolTip(btn, name);
            btn.Click += Accent_Click;
            AccentSwatches.Children.Add(btn);
        }
    }

    private async void Accent_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string hex }) return;
        AppSettings.Current.Accent = hex;
        AppSettings.Current.Save();
        BuildAccentSwatches(hex);
        await new ContentDialog
        {
            Title = "Accent colour saved",
            Content = "Restart PowerX to apply the new accent everywhere.",
            CloseButtonText = "OK",
            XamlRoot = XamlRoot,
        }.ShowAsync();
    }

    private static Windows.UI.Color Hex(string hex)
    {
        var v = uint.Parse(hex, System.Globalization.NumberStyles.HexNumber);
        return Windows.UI.Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    private void Interval_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded || (IntervalBox.SelectedItem as ComboBoxItem)?.Tag is not string tag) return;
        var s = AppSettings.Current;
        s.SamplingMs = int.Parse(tag);
        s.Save();
        TelemetryHub.Instance.Interval = TimeSpan.FromMilliseconds(s.SamplingMs);
    }

    private void Throttle_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!_loaded) return;
        AppSettings.Current.BackgroundThrottle = ThrottleSwitch.IsOn;
        AppSettings.Current.Save();
        if (!ThrottleSwitch.IsOn) TelemetryHub.Instance.Active = true;
    }

    private void Confirm_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!_loaded) return;
        AppSettings.Current.ConfirmProcessActions = ConfirmSwitch.IsOn;
        AppSettings.Current.Save();
    }

    private void AutoUpdate_Toggled(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        if (!_loaded) return;
        AppSettings.Current.AutoCheckUpdates = AutoUpdateSwitch.IsOn;
        AppSettings.Current.Save();
    }

    private async void CheckUpdates_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        CheckUpdatesButton.IsEnabled = false;
        UpdateCheckStatus.Text = "Checking…";
        if (App.Window is not null) await App.Window.CheckForUpdatesAsync(silent: false);
        UpdateCheckStatus.Text = $"Last checked {AppSettings.Current.LastUpdateCheck.LocalDateTime:g}";
        CheckUpdatesButton.IsEnabled = true;
    }

    private async void Report_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        ReportButton.IsEnabled = false;
        ReportButton.Content = "Building…";
        string md;
        try
        {
            md = await Task.Run(() => SystemReport.BuildMarkdown());
        }
        catch (Exception ex)
        {
            App.Log("SystemReport", ex);
            await Info("Could not build the report", ex.Message);
            return;
        }
        finally
        {
            ReportButton.IsEnabled = true;
            ReportButton.Content = "Create system report";
        }

        var box = new TextBox
        {
            Text = md, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap,
            FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace"), FontSize = 12,
            Height = 420, MinWidth = 560,
        };
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Auto);

        var dialog = new ContentDialog
        {
            Title = "System report",
            Content = box,
            PrimaryButtonText = "Save to Desktop",
            SecondaryButtonText = "Copy",
            CloseButtonText = "Close",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        while (true)
        {
            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Secondary)
            {
                var pkg = new Windows.ApplicationModel.DataTransfer.DataPackage();
                pkg.SetText(md);
                Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(pkg);
                continue;   // keep the dialog open so they can also save
            }
            if (result == ContentDialogResult.Primary)
            {
                try
                {
                    var path = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        $"PowerX-report-{DateTime.Now:yyyy-MM-dd-HHmm}.md");
                    File.WriteAllText(path, md);
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    App.Log("SystemReport.Save", ex);
                    await Info("Could not save the report", ex.Message);
                }
            }
            return;
        }
    }

    private async Task Info(string title, string body) => await new ContentDialog
    {
        Title = title, Content = body, CloseButtonText = "OK", XamlRoot = XamlRoot,
    }.ShowAsync();

    private void OpenLogs_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.LogFolder);
            Process.Start(new ProcessStartInfo(AppSettings.LogFolder) { UseShellExecute = true });
        }
        catch (Exception ex) { App.Log("OpenLogs", ex); }
    }

    private async void RestoreTweaks_Click(object sender, Microsoft.UI.Xaml.RoutedEventArgs e)
    {
        var engine = new TweakEngine(TweakCatalog.Default);
        var applied = engine.GetAllStatus().Where(t => t.State == TweakState.Applied).Select(t => t.Definition.Id).ToList();
        if (applied.Count == 0)
        {
            await new ContentDialog { Title = "Nothing to restore", Content = "No PowerX tweaks are currently applied.", CloseButtonText = "OK", XamlRoot = XamlRoot }.ShowAsync();
            return;
        }

        var confirm = await new ContentDialog
        {
            Title = "Restore Windows defaults",
            Content = $"Revert {applied.Count} applied tweak(s) back to the Windows default?",
            PrimaryButtonText = "Restore", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot,
        }.ShowAsync();
        if (confirm != ContentDialogResult.Primary) return;

        var result = await Task.Run(() => engine.ApplyMany(applied, ChangeAction.Revert));
        await new ContentDialog
        {
            Title = "Restore complete",
            Content = $"{result.Succeeded} reverted, {result.AlreadyConfigured} already default, {result.Failed} failed."
                      + (result.Restart.Any ? "\n\nSome changes need Explorer or a sign-out to take effect." : ""),
            CloseButtonText = "OK", XamlRoot = XamlRoot,
        }.ShowAsync();
    }
}
