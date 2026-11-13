using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using PowerX.App.Services;
using PowerX.App.Views;

namespace PowerX.App;

public sealed partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        Title = "PowerX";
        try { AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "PowerX.ico")); }
        catch (Exception ex) { App.Log("SetIcon", ex); }

        var v = typeof(MainWindow).Assembly.GetName().Version;
        FooterVersion.Text = $"PowerX {v?.ToString(3)}";
        FooterElevation.Text = PowerX.Core.Diagnostics.PrivilegeCheck.IsElevated() || Services.DemoData.Active
            ? "administrator" : "⚠ not elevated";

        ApplyTheme();
        ApplyBackdrop();
        var s = AppSettings.Current;
        TelemetryHub.Instance.Interval = TimeSpan.FromMilliseconds(Math.Clamp(s.SamplingMs, 250, 10_000));
        TelemetryHub.Instance.Start();

        VisibilityChanged += (_, e) =>
        {
            if (AppSettings.Current.BackgroundThrottle)
                TelemetryHub.Instance.Active = e.Visible;
        };

        Closed += (_, _) =>
        {
            // A QA run that forced its start page must not become the user's "last page".
            if (Environment.GetEnvironmentVariable("POWERX_START_PAGE") is null)
                AppSettings.Current.LastPage = _currentTag;
            SavePlacement();
            TelemetryHub.Instance.Shutdown();
        };

        // QA helpers: POWERX_WINDOW_SIZE=1900x1100 opens the window at that size;
        // POWERX_START_PAGE=network lands on that page instead of Home.
        if (Environment.GetEnvironmentVariable("POWERX_WINDOW_SIZE") is { } size &&
            size.Split('x') is [var ws, var hs] &&
            int.TryParse(ws, out var winW) && int.TryParse(hs, out var winH))
        {
            _qaSize = new Windows.Graphics.SizeInt32(winW, winH);
            try { AppWindow.Resize(_qaSize.Value); } catch { }
        }
        else
        {
            RestorePlacement();
        }
        TrackPlacement();
        ((FrameworkElement)Content).Loaded += (_, _) => ApplyMinimumSize();

        var startPage = (Environment.GetEnvironmentVariable("POWERX_START_PAGE") ?? StartPageFromSettings())
            ?.Trim().ToLowerInvariant();
        if (!string.IsNullOrEmpty(startPage) && startPage != "home")
        {
            ContentFrame.Navigate(typeof(HomePage));
            Nav.Loaded += (_, _) => Navigate(startPage);
        }
        else
        {
            ContentFrame.Navigate(typeof(HomePage));
        }

        if (AppSettings.Current.AutoCheckUpdates &&
            (DateTimeOffset.UtcNow - AppSettings.Current.LastUpdateCheck).TotalHours > 20)
        {
            _ = CheckForUpdatesAsync(silent: true);
        }

        // Keep a rolling daily configuration snapshot so the "What changed" page has history.
        if (!Services.DemoData.Active)
            _ = Task.Run(() =>
            {
                try { PowerX.Core.Diagnostics.SystemSnapshot.CaptureIfStale(TimeSpan.FromHours(20)); }
                catch (Exception ex) { App.Log("Snapshot", ex); }
            });
    }

    /// <summary>The navigation tag of the page on screen; remembered on close for "Last page used".</summary>
    private string _currentTag = "home";

    private static string StartPageFromSettings()
    {
        var s = AppSettings.Current;
        return s.StartPage == "last" ? (string.IsNullOrEmpty(s.LastPage) ? "home" : s.LastPage) : s.StartPage;
    }

    private string? _updateUrl;
    private PowerX.Core.Diagnostics.UpdateCheckResult? _update;

    public async Task CheckForUpdatesAsync(bool silent)
    {
        var current = typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 1, 0);
        var result = await PowerX.Core.Diagnostics.UpdateChecker.CheckAsync(current);

        // Only a check that worked counts as a check. Stamping a failed one (no network, say)
        // held off the silent retry for 20 hours and let Settings say "Last checked" for a check
        // that never happened.
        var s = AppSettings.Current;
        if (result.Error is null)
        {
            s.LastUpdateCheck = DateTimeOffset.UtcNow;
            s.LastUpdateError = "";
        }
        else
        {
            s.LastUpdateFailure = DateTimeOffset.UtcNow;
            s.LastUpdateError = result.Error;
        }
        s.Save();

        if (result.UpdateAvailable && result.Latest is not null)
        {
            if (silent && AppSettings.Current.DismissedUpdateVersion == result.Latest.ToString()) return;
            _update = result;
            _updateUrl = result.DownloadUrl;
            UpdateBar.Message = $"PowerX {result.Latest} is available (you have {current.ToString(3)}). {result.Notes}";
            UpdateInstallButton.Visibility = result.HasVerifiedInstaller ? Visibility.Visible : Visibility.Collapsed;
            UpdateBar.IsOpen = true;
        }
        else if (!silent)
        {
            await new ContentDialog
            {
                Title = result.Error is null ? "You're up to date" : "Update check failed",
                Content = result.Error ?? $"PowerX {current.ToString(3)} is the latest version.",
                CloseButtonText = "OK",
                XamlRoot = Content.XamlRoot,
            }.ShowAsync();
        }
    }

    private void UpdateBar_Open(object sender, RoutedEventArgs e)
    {
        try
        {
            var r = PowerX.Core.Processes.ShellLauncher.Open(_updateUrl ?? "https://github.com/Nowalski/Power-X/releases");
            if (!r.Success) App.Log("UpdateOpen", new InvalidOperationException(r.Message));
        }
        catch (Exception ex) { App.Log("UpdateOpen", ex); }
    }

    private async void UpdateInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_update is null || !_update.HasVerifiedInstaller) return;

        UpdateInstallButton.IsEnabled = false;
        UpdateReleasesButton.IsEnabled = false;
        UpdateProgress.Visibility = Visibility.Visible;
        UpdateProgress.Value = 0;

        var progress = new Progress<double>(p => UpdateProgress.Value = p);
        var dl = await PowerX.Core.Diagnostics.UpdateInstaller.DownloadVerifiedAsync(_update, progress);

        UpdateProgress.Visibility = Visibility.Collapsed;
        UpdateInstallButton.IsEnabled = true;
        UpdateReleasesButton.IsEnabled = true;

        if (!dl.Ok || dl.Path is null)
        {
            await new ContentDialog
            {
                Title = "Download failed",
                Content = dl.Error ?? "The update could not be downloaded.",
                CloseButtonText = "OK", XamlRoot = Content.XamlRoot,
            }.ShowAsync();
            return;
        }

        var go = await new ContentDialog
        {
            Title = $"Install PowerX {_update.Latest}?",
            Content = "The installer is verified (SHA-256 matches the manifest). PowerX will close so it "
                    + "can update in place; Windows will ask for administrator rights.",
            PrimaryButtonText = "Install now", CloseButtonText = "Later",
            DefaultButton = ContentDialogButton.Primary, XamlRoot = Content.XamlRoot,
        }.ShowAsync();
        if (go != ContentDialogResult.Primary) return;

        var launch = PowerX.Core.Diagnostics.UpdateInstaller.Launch(dl.Path, _update.InstallerSha256 ?? "");
        if (launch.Success)
            Application.Current.Exit();
        else
            await new ContentDialog
            {
                Title = "Could not start the installer",
                Content = launch.Message ?? "Unknown error.",
                CloseButtonText = "OK", XamlRoot = Content.XamlRoot,
            }.ShowAsync();
    }

    private void UpdateBar_Close(InfoBar sender, object args)
    {
        // remember the dismissal so we don't nag about this same version again
        var m = System.Text.RegularExpressions.Regex.Match(sender.Message ?? "", @"PowerX (\d+\.\d+\.\d+)");
        if (m.Success) { AppSettings.Current.DismissedUpdateVersion = m.Groups[1].Value; AppSettings.Current.Save(); }
    }

    /// <summary>Select a nav item by tag (used by in-page links, e.g. Home → Tweaks).</summary>
    public void Navigate(string tag)
    {
        // Settings is the NavigationView's own footer item, not one of MenuItems.
        if (tag == "settings")
        {
            if (Nav.SettingsItem is NavigationViewItem settings) Nav.SelectedItem = settings;
            return;
        }
        foreach (var item in Nav.MenuItems.OfType<NavigationViewItem>())
        {
            if ((string?)item.Tag == tag) { Nav.SelectedItem = item; return; }
        }
    }

    public void ApplyTheme()
    {
        if (Content is FrameworkElement root)
        {
            root.RequestedTheme = AppSettings.Current.Theme switch
            {
                "Light" => ElementTheme.Light,
                "Dark" => ElementTheme.Dark,
                _ => ElementTheme.Default,
            };
        }
    }

    public void ApplyBackdrop()
    {
        bool solid = AppSettings.Current.Backdrop == "None";
        SystemBackdrop = AppSettings.Current.Backdrop switch
        {
            "Acrylic" => new DesktopAcrylicBackdrop(),
            "None" => null,
            _ => new MicaBackdrop(),
        };
        // With no system backdrop the root would be unpainted — give it an opaque
        // theme background. With Mica/Acrylic it must stay transparent to show through.
        if (Content is Panel rootPanel)
            rootPanel.Background = solid
                ? (Brush)Application.Current.Resources["ApplicationPageBackgroundThemeBrush"]
                : null;
    }

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.IsSettingsSelected)
        {
            _currentTag = "settings";
            ContentFrame.Navigate(typeof(SettingsPage), null, new EntranceNavigationTransitionInfo());
            return;
        }

        if ((args.SelectedItemContainer as NavigationViewItem)?.Tag is not string tag) return;
        _currentTag = tag;
        var page = tag switch
        {
            "home" => typeof(HomePage),
            "health" => typeof(HealthCheckPage),
            "processes" => typeof(ProcessesPage),
            "cpu" => typeof(CpuPage),
            "memory" => typeof(MemoryPage),
            "gpu" => typeof(GpuPage),
            "temps" => typeof(TemperaturePage),
            "network" => typeof(NetworkPage),
            "firewall" => typeof(FirewallPage),
            "startup" => typeof(StartupPage),
            "tasks" => typeof(TasksPage),
            "services" => typeof(ServicesPage),
            "drivers" => typeof(DriversPage),
            "events" => typeof(EventLogPage),
            "programs" => typeof(ProgramsPage),
            "tweaks" => typeof(TweaksPage),
            "debloat" => typeof(DebloatPage),
            "repair" => typeof(RepairPage),
            "crashes" => typeof(CrashPage),
            "security" => typeof(SecurityPage),
            "tools" => typeof(ToolsPage),
            "storage" => typeof(StoragePage),
            "changes" => typeof(ChangesPage),
            "history" => typeof(HistoryPage),
            _ => typeof(HomePage),
        };

        try
        {
            if (ContentFrame.CurrentSourcePageType != page)
                ContentFrame.Navigate(page, null, new EntranceNavigationTransitionInfo());
        }
        catch (Exception ex)
        {
            App.Log("Navigate", ex);
        }
    }
}
