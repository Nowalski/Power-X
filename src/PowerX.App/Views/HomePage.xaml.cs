using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PowerX.App.Services;
using PowerX.Core.Diagnostics;
using PowerX.Core.Processes;
using PowerX.Core.Telemetry;
using PowerX.Core.Tweaks;
using QuickActionsCore = PowerX.Core.Diagnostics.QuickActions;

namespace PowerX.App.Views;

public sealed partial class HomePage : Page
{
    private const int TopCount = 6;
    private const int ReorderEveryTicks = 3;

    private readonly ObservableCollection<NameValueVm> _top = [];
    private IDisposable? _subscription;
    private int _tick;

    public HomePage()
    {
        InitializeComponent();
        CpuSpark.Accent = Color.FromArgb(0xFF, 0x4C, 0x8B, 0xF5);
        MemSpark.Accent = Color.FromArgb(0xFF, 0xA9, 0x6B, 0xF0);
        GpuSpark.Accent = Color.FromArgb(0xFF, 0x3F, 0xB9, 0x50);
        TopProcesses.ItemsSource = _top;
        for (int i = 0; i < TopCount; i++) _top.Add(new NameValueVm());

        var info = SystemInfoProvider.Collect();
        SubHeadline.Text = $"{info.WindowsEdition} · build {info.BuildString} · {info.CpuName} · {Fmt.Bytes(info.TotalPhysicalMemory)} RAM"
                           + (info.IsElevated ? "" : "  ·  not running as administrator");

        BuildQuickActions();
        LoadRecommendations();
        PageLayout.CenterCap(this, Root, 1480);
    }

    private void BuildQuickActions()
    {
        QuickActions.Children.Clear();
        AddAction("Restart Explorer", QuickActionsCore.RestartExplorer,
            "Restart Windows Explorer? The taskbar and open Explorer windows will briefly disappear.");
        AddAction("Flush DNS", QuickActionsCore.FlushDns);
        AddAction("Empty Recycle Bin", QuickActionsCore.EmptyRecycleBin,
            "Permanently delete everything in the Recycle Bin?");
        AddAction("Windows Update", () => QuickActionsCore.OpenSettings("ms-settings:windowsupdate"));
        AddAction("Startup apps", () => QuickActionsCore.OpenSettings("ms-settings:startupapps"));
    }

    private void AddAction(string label, Func<ActionResult> run, string? confirm = null)
    {
        var btn = new Button { Content = label };
        btn.Click += async (_, _) =>
        {
            if (confirm is not null)
            {
                var ok = await new ContentDialog
                {
                    Title = label, Content = confirm,
                    PrimaryButtonText = "Continue", CloseButtonText = "Cancel",
                    DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot,
                }.ShowAsync();
                if (ok != ContentDialogResult.Primary) return;
            }

            btn.IsEnabled = false;
            var result = await Task.Run(run);
            btn.IsEnabled = true;
            if (!result.Success)
                await new ContentDialog
                {
                    Title = $"{label} failed", Content = result.Message ?? "Unknown error.",
                    CloseButtonText = "Close", XamlRoot = XamlRoot,
                }.ShowAsync();
        };
        QuickActions.Children.Add(btn);
    }

    private void OpenTweaks_Click(object sender, RoutedEventArgs e) => App.Window?.Navigate("tweaks");

    protected override void OnNavigatedTo(NavigationEventArgs e) => _subscription = TelemetryHub.Instance.Subscribe(OnTick);
    protected override void OnNavigatedFrom(NavigationEventArgs e) => _subscription?.Dispose();

    private void OnTick(object? sender, EventArgs e)
    {
        var hub = TelemetryHub.Instance;

        if (hub.LastCpu?.Value is { } cpu)
        {
            CpuGauge.Value = cpu.TotalUsagePercent;
            CpuGauge.ValueText = $"{cpu.TotalUsagePercent:0}%";
            CpuDetail.Text = $"{cpu.ProcessCount} processes · {cpu.ThreadCount} threads · kernel {cpu.KernelUsagePercent:0.0}% · up {Fmt.Duration(cpu.Uptime)}";
            CpuSpark.SetData(hub.CpuHistory.ToArray(), 100);
        }
        else if (hub.LastCpu is not null)
        {
            CpuGauge.ValueText = "n/a";
            CpuDetail.Text = hub.LastCpu.Detail ?? "CPU data unavailable";
        }

        if (hub.LastMemory?.Value is { } mem)
        {

            MemValue.Text = $"{Fmt.Bytes(mem.InUsePhysical)} / {Fmt.Bytes(mem.TotalPhysical)}";
            MemDetail.Text = $"{mem.UsedPercent:0}% · commit {mem.CommitPercent:0}% · cached {Fmt.Bytes(mem.CachedApprox)}";
            MemSpark.SetData(hub.MemHistory.ToArray(), 100);
        }

        if (hub.LastGpu?.Value is { } gpu)
        {

            GpuValue.Text = $"{gpu.UtilizationPercent,3:0}%";
            GpuDetail.Text = gpu.Engines.Count > 0
                ? $"{gpu.Engines[0].Engine} · VRAM {Fmt.Bytes(gpu.DedicatedMemoryUsed)}"
                : $"VRAM {Fmt.Bytes(gpu.DedicatedMemoryUsed)}";
            GpuSpark.SetData(hub.GpuHistory.ToArray(), 100);
        }
        else if (hub.LastGpu is not null)
        {
            GpuValue.Text = "n/a";
            GpuDetail.Text = "GPU counters unavailable";
        }

        if (hub.LastProcesses is { } snap && _tick++ % ReorderEveryTicks == 0)
        {
            var top = snap.Processes.Where(p => p.Pid > 0)
                .OrderByDescending(p => p.CpuPercent).Take(TopCount).ToList();
            for (int i = 0; i < TopCount; i++)
            {
                if (i < top.Count)
                {
                    _top[i].Name = top[i].Name;
                    _top[i].Value = $"{top[i].CpuPercent,5:0.0}%   {Fmt.Bytes(top[i].WorkingSetBytes)}";
                }
                else { _top[i].Name = ""; _top[i].Value = ""; }
            }
        }
    }

    private void LoadRecommendations()
    {
        var engine = new TweakEngine(TweakCatalog.Default);
        var pending = engine.GetAllStatus()
            .Where(s => s.Definition.Recommended && s.State == TweakState.Default)
            .Select(s => $"›  {s.Definition.Name}")
            .ToList();
        if (pending.Count == 0)
        {
            OpenTweaks.Visibility = Visibility.Collapsed;
            Recommendations.ItemsSource = new List<string> { "No recommended changes outstanding." };
        }
        else
        {
            Recommendations.ItemsSource = pending;
        }
    }
}
