using System.Diagnostics;
using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerX.Core.Diagnostics;
using PowerX.Core.Processes;

namespace PowerX.App.Views;

public sealed partial class ToolsPage : Page
{
    private readonly List<(CleanupTarget target, CheckBox check, TextBlock size, PowerX.App.Controls.LoadBar bar, Windows.UI.Color color)> _cleanup = [];

    private List<EnvVar> _env = [];
    private string _envFilter = "";

    public ToolsPage()
    {
        InitializeComponent();
        PageLayout.CenterCap(this, Root, 1420);
        BuildShortcuts();
        BuildLearn();
        _ = InitAsync();
    }

    private async Task InitAsync()
    {
        // Each block is independent — one failing must not blank the rest of the page.
        Guard("Windows Update status", RefreshWu);
        Guard("System Restore status", RefreshRestore);
        Guard("environment variables", LoadEnv);
        Guard("pending restart", RefreshRebootState);

        // process spawn + WMI — off the UI thread so navigating to Tools stays snappy
        try { await RefreshPowerAsync(); } catch (Exception ex) { App.Log("Tools.Power", ex); }
        try { await BuildDisksAsync(); } catch (Exception ex) { App.Log("Tools.Disks", ex); }
        try { await ScanCleanupAsync(); } catch (Exception ex) { App.Log("Tools.Cleanup", ex); }
        try { await RefreshBatteryAsync(); } catch (Exception ex) { App.Log("Tools.Battery", ex); }
    }

    // ---------------------------------------------------------------- pending restart

    private void RefreshRebootState()
    {
        var status = Services.DemoData.Active ? Services.DemoData.PendingReboot() : PendingReboot.Check();
        RebootBar.IsOpen = status.Pending;
        RebootReasons.Text = status.Pending
            ? string.Join("\n", status.Reasons.Select(r => "- " + r))
            : "";
    }

    private void RebootRecheck_Click(object sender, RoutedEventArgs e) => Guard("pending restart", RefreshRebootState);

    private async void RestartNow_Click(object sender, RoutedEventArgs e)
    {
        var confirm = new ContentDialog
        {
            Title = "Restart now?",
            Content = "Windows will close your open apps and restart. Save your work first.",
            PrimaryButtonText = "Restart", CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot,
        };
        if (await confirm.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5 /d p:0:0") { UseShellExecute = false, CreateNoWindow = true });
        }
        catch (Exception ex) { App.Log("Tools.Restart", ex); }
    }

    // ---------------------------------------------------------------- component store

    private CancellationTokenSource? _winsxsCts;

    private async void WinSxsAnalyze_Click(object sender, RoutedEventArgs e)
    {
        WinSxsAnalyzeButton.IsEnabled = false;
        WinSxsText.Text = "Analyzing the component store. This can take a minute...";
        try
        {
            var info = Services.DemoData.Active
                ? Services.DemoData.ComponentStore()
                : await ComponentStore.AnalyzeAsync();

            if (info.Error is not null)
            {
                WinSxsText.Text = "Could not analyze the component store: " + info.Error;
            }
            else
            {
                WinSxsText.Text =
                    $"Actual size on disk: {Fmt.Bytes((ulong)info.ActualSizeBytes)}   "
                    + $"(shared with Windows: {Fmt.Bytes((ulong)info.SharedWithWindowsBytes)}).\n"
                    + $"Reclaimable: backups and disabled features {Fmt.Bytes((ulong)info.BackupsAndDisabledBytes)}, "
                    + $"cache and temp {Fmt.Bytes((ulong)info.CacheAndTempBytes)}. "
                    + $"{info.ReclaimablePackages} superseded package(s).\n"
                    + (info.CleanupRecommended
                        ? "Windows recommends a component cleanup."
                        : "Windows does not think a cleanup is needed right now.")
                    + (info.LastCleanup is { } d ? $"  Last cleanup {d.LocalDateTime:d}." : "");
                WinSxsCleanButton.IsEnabled = info.ReclaimablePackages > 0 || info.CleanupRecommended;
            }
        }
        catch (Exception ex)
        {
            App.Log("Tools.WinSxs", ex);
            WinSxsText.Text = "Could not analyze the component store: " + ex.Message;
        }
        finally
        {
            WinSxsAnalyzeButton.IsEnabled = true;
        }
    }

    private async void WinSxsClean_Click(object sender, RoutedEventArgs e)
    {
        if (_winsxsCts is not null) return;
        _winsxsCts = new CancellationTokenSource();
        var cts = _winsxsCts;
        WinSxsOut.Visibility = Visibility.Visible;
        WinSxsOut.Text = "";
        WinSxsCleanButton.IsEnabled = false;
        WinSxsAnalyzeButton.IsEnabled = false;
        WinSxsStopButton.IsEnabled = true;

        void Line(string s) => DispatcherQueue.TryEnqueue(() =>
        {
            WinSxsOut.Text += s + "\n";
            WinSxsOut.Select(WinSxsOut.Text.Length, 0);
        });

        try
        {
            if (Services.DemoData.Active) { Line("Component cleanup finished. (demo)"); }
            else await ComponentStore.StartCleanupAsync(Line, cts.Token);
        }
        catch (Exception ex)
        {
            App.Log("Tools.WinSxsClean", ex);
            Line("error: " + ex.Message);
        }
        finally
        {
            cts.Dispose();
            _winsxsCts = null;
            WinSxsStopButton.IsEnabled = false;
            WinSxsAnalyzeButton.IsEnabled = true;
        }
    }

    private void WinSxsStop_Click(object sender, RoutedEventArgs e) => _winsxsCts?.Cancel();

    // ---------------------------------------------------------------- battery

    private async Task RefreshBatteryAsync()
    {
        var info = Services.DemoData.Active ? Services.DemoData.Battery() : await BatteryHealth.ReadAsync();
        if (!info.HasBattery) { BatteryCard.Visibility = Visibility.Collapsed; return; }
        BatteryCard.Visibility = Visibility.Visible;

        string charge = info.OnAcPower
            ? (info.Charging ? $"{info.ChargePercent}%, charging" : $"{info.ChargePercent}%, plugged in")
            : $"{info.ChargePercent}% on battery"
              + (info.EstimatedRuntime is { } rt ? $", about {Fmt.Duration(rt)} left" : "");

        BatteryHeadline.Text = info.WearPercent > 0
            ? $"Battery health: {info.Health}, {info.WearPercent}% of original capacity lost"
            : $"Battery: {charge}";

        var parts = new List<string> { charge };
        if (info.DesignCapacityMwh > 0)
            parts.Add($"full charge holds {info.FullChargeCapacityMwh:N0} mWh of the original {info.DesignCapacityMwh:N0} mWh");
        if (info.CycleCount > 0) parts.Add($"{info.CycleCount} charge cycles");
        if (info.Error is not null) parts.Add(info.Error);
        BatteryDetail.Text = string.Join(".  ", parts) + ".";
    }

    private void BatteryRefresh_Click(object sender, RoutedEventArgs e) =>
        _ = RefreshBatteryAsync();

    private async void BatteryReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string outPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PowerX-battery-report.html");
            using (var p = Process.Start(new ProcessStartInfo("powercfg.exe", $"/batteryreport /output \"{outPath}\"")
            { UseShellExecute = false, CreateNoWindow = true }))
            {
                if (p is not null) await p.WaitForExitAsync();
            }
            if (System.IO.File.Exists(outPath))
                Process.Start(new ProcessStartInfo(outPath) { UseShellExecute = true });
        }
        catch (Exception ex) { App.Log("Tools.BatteryReport", ex); }
    }

    private static void Guard(string what, Action step)
    {
        try { step(); }
        catch (Exception ex) { App.Log($"Tools.{what}", ex); }
    }

    // ---------------------------------------------------------------- windows update

    private void RefreshWu()
    {
        var st = WindowsUpdateControl.Status();
        (WuStatus.Text, WuBadge.Style, WuDisableButton.IsEnabled) = st.State switch
        {
            WindowsUpdateState.Disabled => ("Disabled. This machine is not receiving updates.",
                (Style)Application.Current.Resources["CriticalDotInfoBadgeStyle"], false),
            WindowsUpdateState.Paused => ($"Paused until {st.PausedUntil?.LocalDateTime:d}.",
                (Style)Application.Current.Resources["CautionIconInfoBadgeStyle"], true),
            _ => ("Active. Updates install normally.",
                (Style)Application.Current.Resources["SuccessDotInfoBadgeStyle"], true),
        };
    }

    private async void WuPause_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag } || !int.TryParse(tag, out int days)) return;
        var r = await Task.Run(() => WindowsUpdateControl.Pause(days));
        if (!r.Success) await Info("Pause failed", r.Message);
        RefreshWu();
    }

    private async void WuDisable_Click(object sender, RoutedEventArgs e)
    {
        if (!await Confirm("Disable Windows Update completely?",
                "The machine will stop downloading security and quality updates until you choose Restore. " +
                "This is a security trade-off. Continue?")) return;
        var r = await Task.Run(WindowsUpdateControl.Disable);
        if (!r.Success) await Info("Could not disable Windows Update", r.Message);
        else await Info("Windows Update disabled", "Update services, tasks and policy have been turned off. Use Restore defaults to undo this, then restart.");
        RefreshWu();
    }

    private async void WuRestore_Click(object sender, RoutedEventArgs e)
    {
        var r = await Task.Run(WindowsUpdateControl.Restore);
        if (!r.Success) await Info("Restore failed", r.Message);
        else await Info("Windows Update restored", "Services, tasks, policy and any pause have been reset to Windows defaults. A restart is recommended.");
        RefreshWu();
    }

    // ---------------------------------------------------------------- system restore

    private void RefreshRestore()
    {
        bool on = SystemRestore.IsEnabled();
        RestoreStatus.Text = on
            ? "System Protection is on for this PC."
            : "System Protection appears to be off. Creating a point will try to turn it on.";
    }

    private async void CreateRp_Click(object sender, RoutedEventArgs e)
    {
        CreateRpButton.IsEnabled = false;
        var r = await Task.Run(() => SystemRestore.Create($"PowerX manual point {DateTime.Now:g}"));
        CreateRpButton.IsEnabled = true;
        await Info(r.Success ? "Restore point created" : "Could not create a restore point",
            r.Success ? "You can roll back to it from Windows' System Restore if needed." : r.Message);
        RefreshRestore();
    }

    private async void ListRp_Click(object sender, RoutedEventArgs e)
    {
        var points = await Task.Run(() => SystemRestore.List());
        var panel = new StackPanel { Spacing = 6, Width = 460 };
        if (points.Count == 0)
            panel.Children.Add(new TextBlock { Text = "No restore points found.", Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
        foreach (var p in points.Take(30))
            panel.Children.Add(new TextBlock
            {
                Text = $"{p.Created.LocalDateTime:g}  ·  {p.Type}\n{p.Description}",
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4),
            });

        var choice = await new ContentDialog
        {
            Title = "Restore points",
            Content = new ScrollViewer { Content = panel, MaxHeight = 380 },
            PrimaryButtonText = "Open System Restore",
            CloseButtonText = "Close",
            XamlRoot = XamlRoot,
        }.ShowAsync();
        if (choice == ContentDialogResult.Primary) SystemRestore.OpenRestoreUi();
    }

    private void OpenRp_Click(object sender, RoutedEventArgs e) => SystemRestore.OpenRestoreUi();

    // ---------------------------------------------------------------- environment variables

    private void LoadEnv()
    {
        _env = EnvironmentVariables.All().ToList();
        EnvSummary.Text = $"{_env.Count(v => !v.Machine)} user · {_env.Count(v => v.Machine)} machine";
        RenderEnv();
    }

    private void EnvFilter_Changed(object sender, TextChangedEventArgs e)
    {
        _envFilter = EnvFilter.Text.Trim();
        RenderEnv();
    }

    private void RenderEnv()
    {
        var shown = _env.Where(v => _envFilter.Length == 0
            || v.Name.Contains(_envFilter, StringComparison.OrdinalIgnoreCase)
            || v.Value.Contains(_envFilter, StringComparison.OrdinalIgnoreCase));

        var items = new List<UIElement>();
        foreach (var v in shown.Take(200))
        {
            var edit = new HyperlinkButton { Content = "edit", Padding = new Thickness(4, 0, 4, 0) };
            edit.Click += async (_, _) => await EditEnv(v);
            var grid = new Grid { ColumnSpacing = 8, Padding = new Thickness(0, 3, 0, 3) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var scope = new TextBlock { Text = v.Machine ? "M" : "U", FontSize = 11, Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"] };
            var name = new TextBlock { Text = v.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis };
            var val = new TextBlock { Text = v.Value, FontSize = 11, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace") };
            Grid.SetColumn(name, 1); Grid.SetColumn(val, 2); Grid.SetColumn(edit, 3);
            grid.Children.Add(scope); grid.Children.Add(name); grid.Children.Add(val); grid.Children.Add(edit);
            items.Add(grid);
        }
        EnvList.ItemsSource = items;
    }

    private async void EnvAdd_Click(object sender, RoutedEventArgs e) => await EditEnv(null);

    private async Task EditEnv(EnvVar? existing)
    {
        var name = new TextBox { Header = "Name", Text = existing?.Name ?? "", IsEnabled = existing is null };
        var value = new TextBox { Header = "Value", Text = existing?.Value ?? "", AcceptsReturn = true, Height = 90, TextWrapping = TextWrapping.Wrap };
        var scope = new ComboBox { Header = "Scope", Items = { "User", "Machine (all users)" }, SelectedIndex = existing?.Machine == true ? 1 : 0, IsEnabled = existing is null };
        var panel = new StackPanel { Spacing = 10, Width = 460, Children = { name, value, scope } };

        var dialog = new ContentDialog
        {
            Title = existing is null ? "New environment variable" : $"Edit {existing.Name}",
            Content = panel,
            PrimaryButtonText = "Save",
            SecondaryButtonText = existing is null ? "" : "Delete",
            CloseButtonText = "Cancel",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };

        var result = await dialog.ShowAsync();
        bool machine = existing?.Machine ?? scope.SelectedIndex == 1;

        if (result == ContentDialogResult.Primary)
        {
            var r = await Task.Run(() => EnvironmentVariables.Set(name.Text, value.Text, machine));
            if (!r.Success) await Info("Could not save", r.Message);
        }
        else if (result == ContentDialogResult.Secondary && existing is not null)
        {
            var r = await Task.Run(() => EnvironmentVariables.Delete(existing.Name, machine));
            if (!r.Success) await Info("Could not delete", r.Message);
        }
        else return;

        LoadEnv();
    }

    // ---------------------------------------------------------------- shortcuts

    private void BuildShortcuts()
    {
        void Add(Panel row, string label, Action act)
        {
            var b = new Button { Content = label };
            b.Click += (_, _) => { try { act(); } catch (Exception ex) { App.Log("shortcut", ex); } };
            row.Children.Add(b);
        }

        void Open(string target) => Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });

        void Run(string file, string args) => Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true });

        Add(ShortcutRow1, "Hosts file", () => Run("notepad.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts")));
        Add(ShortcutRow1, "God Mode", () =>
        {
            var p = Path.Combine(Path.GetTempPath(), "GodMode.{ED7BA470-8E54-465E-825C-99712043E01C}");
            Directory.CreateDirectory(p);
            Open(p);
        });
        Add(ShortcutRow1, "Startup folder", () => Open("shell:startup"));
        Add(ShortcutRow1, "Temp folder", () => Open(Path.GetTempPath()));
        Add(ShortcutRow1, "SoftwareDistribution", () => Open(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SoftwareDistribution", "Download")));
        Add(ShortcutRow1, "Fonts", () => Open("shell:fonts"));

        Add(ShortcutRow2, "Event Viewer", () => Open("eventvwr.msc"));
        Add(ShortcutRow2, "Reliability Monitor", () => Run("perfmon.exe", "/rel"));
        Add(ShortcutRow2, "Resource Monitor", () => Open("resmon.exe"));
        Add(ShortcutRow2, "Performance Monitor", () => Open("perfmon.exe"));
        Add(ShortcutRow2, "DirectX Diagnostics", () => Open("dxdiag.exe"));
        Add(ShortcutRow2, "System Info", () => Open("msinfo32.exe"));
        Add(ShortcutRow2, "Memory Diagnostic", () => Open("MdSched.exe"));

        Add(ShortcutRow3, "Device Manager", () => Open("devmgmt.msc"));
        Add(ShortcutRow3, "Services", () => Open("services.msc"));
        Add(ShortcutRow3, "Task Scheduler", () => Open("taskschd.msc"));
        Add(ShortcutRow3, "Disk Management", () => Open("diskmgmt.msc"));
        Add(ShortcutRow3, "Group Policy", () => Open("gpedit.msc"));
        Add(ShortcutRow3, "Registry Editor", () => Open("regedit.exe"));
        Add(ShortcutRow3, "Local Users & Groups", () => Open("lusrmgr.msc"));
        Add(ShortcutRow3, "Programs & Features", () => Open("appwiz.cpl"));
        Add(ShortcutRow3, "Optional Features", () => Open("optionalfeatures.exe"));
        Add(ShortcutRow3, "Advanced system properties", () => Open("SystemPropertiesAdvanced.exe"));

        Add(ShortcutRow4, "Windows Update", () => Open("ms-settings:windowsupdate"));
        Add(ShortcutRow4, "Startup apps", () => Open("ms-settings:startupapps"));
        Add(ShortcutRow4, "Storage Sense", () => Open("ms-settings:storagesense"));
        Add(ShortcutRow4, "Privacy dashboard", () => Open("ms-settings:privacy"));
        Add(ShortcutRow4, "Graphics settings", () => Open("ms-settings:display-advancedgraphics"));
        Add(ShortcutRow4, "For developers", () => Open("ms-settings:developers"));
        Add(ShortcutRow4, "Windows Security", () => Open("windowsdefender:"));
    }

    private void BuildLearn()
    {
        (string Title, string Body)[] notes =
        [
            ("\"Gamer tweaks\" rarely help",
             "Disabling services, forcing power plans and registry \"FPS boosts\" shared on forums almost never produce a measurable, repeatable frame-rate gain on a healthy modern PC. The reliable wins are: fewer startup and background apps, current GPU drivers, adequate free disk space and RAM, and per-game graphics settings. PowerX only ships tweaks with a stated rationale."),
            ("Telemetry vs. security updates",
             "You can reduce diagnostic data (Settings > Privacy) and remove advertising IDs without touching security. Turning off Windows Update, Defender, SmartScreen or the firewall is a different category. It removes protection, not spying. PowerX labels those as security trade-offs and never puts them in a default profile."),
            ("Debloat safely",
             "Removing a Store app is reversible: reinstall it from the Store or winget. Removing shell components, capabilities or Features on Demand can break Windows in ways that need an in-place repair install. PowerX's debloat list is Store and consumer apps only and never pre-selects anything."),
            ("Restore points are not backups",
             "System Restore snapshots system files and the registry, not your documents. Keep a real file backup (File History, an external drive, or cloud sync) as well. Create a restore point before driver updates or a batch of tweaks."),
            ("Page file / virtual memory",
             "Leave it on system managed. A fixed size or a disabled page file can cause crashes when RAM fills, and gives no speed benefit on SSDs. More physical RAM is the real fix for heavy multitasking."),
            ("SSD optimization",
             "Modern Windows already sends TRIM to SSDs and does not defragment them. Third-party SSD optimizers that disable prefetch, superfetch or the page file can make things slower. The one useful habit is keeping 10 to 15% of the drive free."),
        ];

        foreach (var (title, body) in notes)
        {
            var sp = new StackPanel { Spacing = 2 };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
            sp.Children.Add(new TextBlock
            {
                Text = body, FontSize = 12, TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            });
            LearnList.Children.Add(sp);
        }
    }

    // ---------------------------------------------------------------- power

    private async Task RefreshPowerAsync()
    {
        var active = await Task.Run(() => PowerPlans.List().FirstOrDefault(p => p.Active));
        PowerCurrent.Text = active is null ? "Current plan: unknown" : $"Current plan: {active.Name}";
    }

    private async void Balanced_Click(object s, RoutedEventArgs e) => await SetPlan(PowerPlans.Balanced);
    private async void HighPerf_Click(object s, RoutedEventArgs e) => await SetPlan(PowerPlans.HighPerformance);
    private async void Saver_Click(object s, RoutedEventArgs e) => await SetPlan(PowerPlans.PowerSaver);

    private async void Ultimate_Click(object s, RoutedEventArgs e)
    {
        var (result, id) = await Task.Run(PowerPlans.EnsureUltimatePerformance);
        if (id is { } g) await SetPlan(g);
        else await Info("Ultimate performance", result.Message ?? "Could not create the plan.");
    }

    private async Task SetPlan(Guid id)
    {
        var r = await Task.Run(() => PowerPlans.Activate(id));
        await RefreshPowerAsync();
        if (!r.Success) await Info("Power plan", r.Message);
    }

    // ---------------------------------------------------------------- cleanup

    private static readonly Windows.UI.Color[] CleanupPalette =
    [
        Windows.UI.Color.FromArgb(0xFF, 0x4C, 0x8B, 0xF5),
        Windows.UI.Color.FromArgb(0xFF, 0x33, 0xB0, 0xA6),
        Windows.UI.Color.FromArgb(0xFF, 0xD9, 0xC0, 0x40),
        Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x8A, 0x3A),
        Windows.UI.Color.FromArgb(0xFF, 0xA9, 0x6B, 0xF0),
        Windows.UI.Color.FromArgb(0xFF, 0x6C, 0xC0, 0x5A),
        Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x5F, 0x8A),
        Windows.UI.Color.FromArgb(0xFF, 0x5A, 0xC8, 0xE0),
        Windows.UI.Color.FromArgb(0xFF, 0xC0, 0x8A, 0x5A),
        Windows.UI.Color.FromArgb(0xFF, 0x8A, 0xC0, 0x40),
        Windows.UI.Color.FromArgb(0xFF, 0xE0, 0x4F, 0x4F),
        Windows.UI.Color.FromArgb(0xFF, 0x6B, 0x8A, 0xF0),
        Windows.UI.Color.FromArgb(0xFF, 0xB0, 0x60, 0xC8),
    ];

    private async Task ScanCleanupAsync()
    {
        var targets = CleanupScanner.BuildTargets();
        CleanupList.Children.Clear();
        _cleanup.Clear();

        int idx = 0;
        foreach (var t in targets)
        {
            var color = CleanupPalette[idx % CleanupPalette.Length];
            var check = new CheckBox { IsChecked = t.RecommendedDefault, VerticalAlignment = VerticalAlignment.Center, MinWidth = 30 };
            check.Checked += (_, _) => UpdateCleanTotal();
            check.Unchecked += (_, _) => UpdateCleanTotal();

            var dot = new Microsoft.UI.Xaml.Shapes.Ellipse { Width = 9, Height = 9, Fill = new SolidColorBrush(color), VerticalAlignment = VerticalAlignment.Center };
            var size = new TextBlock { Text = "…", Style = (Style)Application.Current.Resources["MonoStyle"], VerticalAlignment = VerticalAlignment.Center, MinWidth = 68, TextAlignment = TextAlignment.Right };
            var bar = new PowerX.App.Controls.LoadBar { Value = 0, Width = 150, VerticalAlignment = VerticalAlignment.Center };

            var text = new StackPanel { Spacing = 1 };
            text.Children.Add(new TextBlock { Text = t.Name, FontSize = 13 });
            text.Children.Add(new TextBlock { Text = t.Description, FontSize = 11, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });

            // check | dot | name+desc (fills) | bar (150) | size (68) — the meter stays next to its number
            var grid = new Grid { ColumnSpacing = 12, Padding = new Thickness(0, 6, 0, 6) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 160 });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetColumn(check, 0); Grid.SetColumn(dot, 1); Grid.SetColumn(text, 2); Grid.SetColumn(bar, 3); Grid.SetColumn(size, 4);
            grid.Children.Add(check); grid.Children.Add(dot); grid.Children.Add(text); grid.Children.Add(bar); grid.Children.Add(size);
            CleanupList.Children.Add(grid);
            _cleanup.Add((t, check, size, bar, color));
            idx++;
        }

        CleanupTotal.Text = "Scanning…";
        await Task.Run(() =>
        {
            foreach (var c in _cleanup) CleanupScanner.Measure(c.target);
        });

        long max = _cleanup.Count == 0 ? 1 : Math.Max(1, _cleanup.Max(c => c.target.SizeBytes));
        foreach (var c in _cleanup)
        {
            c.size.Text = c.target.FileCount == 0 ? "empty" : Fmt.Bytes((ulong)c.target.SizeBytes);
            c.bar.Value = 100.0 * c.target.SizeBytes / max;
        }
        RenderCleanupBar();
        UpdateCleanTotal();
    }

    private void RenderCleanupBar()
    {
        CleanupBar.SetSegments(_cleanup
            .Where(c => c.target.SizeBytes > 0)
            .Select(c => new PowerX.App.Controls.Segment(c.target.Name, c.target.SizeBytes, c.color))
            .ToList());
    }

    private void UpdateCleanTotal()
    {
        long sel = _cleanup.Where(c => c.check.IsChecked == true).Sum(c => c.target.SizeBytes);
        long all = _cleanup.Sum(c => c.target.SizeBytes);
        CleanupTotal.Text = all == 0
            ? "Nothing to clean up right now"
            : $"{Fmt.Bytes((ulong)sel)} selected  ·  {Fmt.Bytes((ulong)all)} reclaimable in total";
        CleanButton.IsEnabled = sel > 0;
    }

    private CancellationTokenSource? _cleanCts;

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        // While a clean is running the button becomes "Stop".
        if (_cleanCts is not null) { _cleanCts.Cancel(); return; }

        var picked = _cleanup.Where(c => c.check.IsChecked == true).ToList();
        if (picked.Count == 0) return;
        long totalSel = picked.Sum(c => c.target.SizeBytes);
        if (!await Confirm("Delete selected files?",
                $"This permanently deletes about {Fmt.Bytes((ulong)totalSel)} across {picked.Count} location(s). Files still in use are skipped.")) return;

        _cleanCts = new CancellationTokenSource();
        var ct = _cleanCts.Token;
        string cleanLabel = (string)CleanButton.Content;
        CleanButton.Content = "Stop";
        CleanButton.IsEnabled = true;
        CleanProgress.Visibility = Visibility.Visible;
        CleanProgressText.Visibility = Visibility.Visible;
        CleanProgress.IsIndeterminate = false;
        CleanProgress.Value = 0;

        long freedTotal = 0;
        int deleted = 0, failed = 0;
        try
        {
            foreach (var c in picked)
            {
                if (ct.IsCancellationRequested) break;
                long baseFreed = freedTotal;
                var progress = new Progress<long>(freed =>
                {
                    double pct = totalSel == 0 ? 0 : 100.0 * (baseFreed + freed) / totalSel;
                    CleanProgress.Value = Math.Clamp(pct, 0, 100);
                    CleanProgressText.Text = $"Cleaning {c.target.Name}, freed {Fmt.Bytes((ulong)(baseFreed + freed))}";
                });
                var (f, d, x) = await Task.Run(() => CleanupScanner.Clean(c.target, progress, ct));
                freedTotal += f; deleted += d; failed += x;
                c.bar.Value = 0;
                c.check.IsChecked = false;
            }
        }
        finally
        {
            _cleanCts.Dispose();
            _cleanCts = null;
            CleanButton.Content = cleanLabel;
        }

        CleanProgress.Value = ct.IsCancellationRequested ? CleanProgress.Value : 100;
        CleanProgressText.Text = (ct.IsCancellationRequested ? "Stopped. " : "")
            + $"freed {Fmt.Bytes((ulong)freedTotal)} · {deleted} files"
            + (failed > 0 ? $" · {failed} in use, skipped" : "");
        await Task.Delay(1400);
        CleanProgress.Visibility = Visibility.Collapsed;
        CleanProgressText.Visibility = Visibility.Collapsed;

        await ScanCleanupAsync();
    }

    // ---------------------------------------------------------------- disks

    private async Task BuildDisksAsync()
    {
        DiskList.Children.Add(new TextBlock { Text = "Reading disks…", Style = (Style)Application.Current.Resources["MutedStyle"] });
        var (volumes, disks) = await Task.Run(() => (StorageInfo.Volumes(), StorageInfo.PhysicalDisks()));
        DiskList.Children.Clear();

        foreach (var d in disks)
        {
            var head = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            head.Children.Add(new TextBlock { Text = d.Name, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
            head.Children.Add(Chip(d.MediaType, (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]));
            head.Children.Add(Chip(d.BusType, (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]));
            head.Children.Add(Chip(d.Health, d.Health == "Healthy"
                ? (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
                : (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]));

            var sp = new StackPanel { Spacing = 2 };
            sp.Children.Add(head);
            var detail = $"{Fmt.Bytes(d.SizeBytes)}";
            if (d.TemperatureC is { } tC) detail += $"  ·  {tC} °C";
            if (d.WearPercent is { } w) detail += $"  ·  {w}% endurance used";
            sp.Children.Add(new TextBlock { Text = detail, FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
            DiskList.Children.Add(sp);
        }

        foreach (var v in volumes)
        {
            var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = v.UsedPercent, Margin = new Thickness(0, 2, 0, 0) };
            var sp = new StackPanel { Spacing = 2, Margin = new Thickness(0, 4, 0, 0) };
            sp.Children.Add(new TextBlock
            {
                Text = $"{v.Drive}  {v.Label} · {v.FileSystem} · {Fmt.Bytes(v.FreeBytes)} free of {Fmt.Bytes(v.TotalBytes)}",
                FontSize = 12,
            });
            sp.Children.Add(bar);
            DiskList.Children.Add(sp);
        }

        if (DiskList.Children.Count == 0)
            DiskList.Children.Add(new TextBlock { Text = "No fixed disks detected.", Style = (Style)Application.Current.Resources["MutedStyle"] });
    }

    private void DiskMgmt_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("diskmgmt.msc") { UseShellExecute = true }); }
        catch (Exception ex) { App.Log("DiskMgmt", ex); }
    }

    // ---------------------------------------------------------------- helpers

    private async Task<bool> Confirm(string title, string body) => await new ContentDialog
    {
        Title = title, Content = body,
        PrimaryButtonText = "Continue", CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot,
    }.ShowAsync() == ContentDialogResult.Primary;

    private async Task Info(string title, string? body) => await new ContentDialog
    {
        Title = title, Content = body ?? "", CloseButtonText = "OK", XamlRoot = XamlRoot,
    }.ShowAsync();

    private static Border Chip(string text, Brush fg) => new()
    {
        Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
        CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 1, 7, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = fg },
    };
}
