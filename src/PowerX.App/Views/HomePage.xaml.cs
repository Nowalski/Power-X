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
        bool showElevationWarning = !info.IsElevated && !DemoData.Active;
        SubHeadline.Text = $"{info.WindowsEdition} · build {info.BuildString} · {info.CpuName} · {Fmt.Bytes(info.TotalPhysicalMemory)} RAM"
                           + (showElevationWarning ? "  ·  not running as administrator" : "");

        BuildQuickActions();
        LoadRecommendations();
        PageLayout.LeftCap(this, Root, 1480);
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
        AddCopySystemInfo();
    }

    /// <summary>
    /// Copies a short plain-text spec sheet (Windows, CPU, memory, GPU, uptime) for pasting into
    /// a forum post or a support chat. No machine name or user name: it is meant to be shared.
    /// </summary>
    private void AddCopySystemInfo()
    {
        var btn = new Button { Content = "Copy system info" };
        ToolTipService.SetToolTip(btn, "Copy Windows, CPU, memory, GPU and uptime as text, for a forum post or support chat. No machine or user name.");
        btn.Click += async (_, _) =>
        {
            bool ok = Services.Clip.SetText(SystemInfoText());
            btn.Content = ok ? "Copied ✓" : "Clipboard busy, try again";
            await Task.Delay(1800);
            btn.Content = "Copy system info";
        };
        QuickActions.Children.Add(btn);
    }

    internal static string SystemInfoText()
    {
        var info = SystemInfoProvider.Collect();
        var hub = TelemetryHub.Instance;
        var sb = new System.Text.StringBuilder();
        string version = string.IsNullOrEmpty(info.DisplayVersion) ? "" : $" {info.DisplayVersion}";
        sb.AppendLine($"OS:      {info.WindowsEdition}{version} (build {info.BuildString}, {info.Architecture})");
        sb.AppendLine($"CPU:     {info.CpuName} ({info.LogicalProcessors} logical processors)");
        sb.AppendLine($"Memory:  {Fmt.Bytes(info.TotalPhysicalMemory)}");
        foreach (var gpu in hub.GpuAdapters)
        {
            string vram = gpu.DedicatedMemoryTotal > 0 ? $", {Fmt.Bytes(gpu.DedicatedMemoryTotal)} VRAM" : "";
            string driver = string.IsNullOrEmpty(gpu.DriverVersion) ? "" : $", driver {gpu.DriverVersion}";
            sb.AppendLine($"GPU:     {gpu.Name}{vram}{driver}");
        }
        if (hub.LastCpu?.Value is { } cpu)
            sb.AppendLine($"Uptime:  {Fmt.Duration(cpu.Uptime)}");
        var v = typeof(HomePage).Assembly.GetName().Version;
        sb.Append($"(PowerX {v?.ToString(3)})");
        return sb.ToString();
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
