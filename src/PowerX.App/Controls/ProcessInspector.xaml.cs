using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerX.App.Services;
using PowerX.Core.Processes;
using PowerX.Core.Telemetry;

namespace PowerX.App.Controls;

public sealed record KV(string K, string V);

public sealed partial class ProcessInspector : UserControl, IDisposable
{
    private readonly int _pid;
    private readonly MetricRing _cpu = new(90);
    private readonly MetricRing _mem = new(90);
    private IDisposable? _subscription;
    private bool _modulesLoaded;

    public ProcessInspector(int pid, string name)
    {
        InitializeComponent();
        _pid = pid;
        HeaderName.Text = name;
        HeaderPid.Text = $"PID {pid}";
        CpuChart.Accent = Color.FromArgb(0xFF, 0x4C, 0x8B, 0xF5);
        CpuChart.MaxLabel = "100%";
        MemChart.Accent = Color.FromArgb(0xFF, 0xA9, 0x6B, 0xF0);

        _ = LoadOverviewAsync();
        _subscription = TelemetryHub.Instance.Subscribe(OnTick);
    }

    private async Task LoadOverviewAsync()
    {
        var d = await Task.Run(() => ProcessDetailsProvider.Resolve(_pid));
        var info = TelemetryHub.Instance.LastProcesses?.Processes.FirstOrDefault(p => p.Pid == _pid);

        Chips.Children.Clear();
        if (d.IsElevated == true) Chips.Children.Add(Chip("Elevated", (Brush)Application.Current.Resources["SystemFillColorCautionBrush"]));
        if (d.Company is not null) Chips.Children.Add(Chip(d.Company, (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]));
        if (d.ImagePath is null) Chips.Children.Add(Chip("path unavailable", (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]));

        var about = PowerX.Core.Processes.ProcessKnowledge.Explain(HeaderName.Text, d.ImagePath, d.Company);
        var rows = new List<KV>
        {
            new("About", about.Summary),
            new("Description", d.Description ?? "—"),
            new("Company", d.Company ?? "—"),
            new("Version", d.Version ?? "—"),
            new("Path", d.ImagePath ?? "unavailable"),
            new("Elevated", d.IsElevated is null ? "unknown" : d.IsElevated.Value ? "yes" : "no"),
            new("Integrity", string.IsNullOrEmpty(d.IntegrityLevel) ? "unknown" : d.IntegrityLevel),
        };
        if (info is not null)
        {
            rows.Add(new("Parent PID", info.ParentPid.ToString()));
            rows.Add(new("Session", info.SessionId.ToString()));
            rows.Add(new("Threads", info.ThreadCount.ToString()));
            rows.Add(new("Handles", info.HandleCount.ToString()));
            rows.Add(new("Base priority", info.BasePriority.ToString()));
            rows.Add(new("Started", info.StartTime?.LocalDateTime.ToString() ?? "unknown"));
            rows.Add(new("CPU time", $"{info.TotalProcessorTime:g}"));
            rows.Add(new("Working set", Fmt.Bytes(info.WorkingSetBytes)));
            rows.Add(new("Private bytes", Fmt.Bytes(info.PrivateBytes)));
        }
        Overview.ItemsSource = rows;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        var info = TelemetryHub.Instance.LastProcesses?.Processes.FirstOrDefault(p => p.Pid == _pid);
        if (info is null)
        {
            PerfCpu.Text = "exited";
            return;
        }
        _cpu.Add(info.CpuPercent);
        _mem.Add(info.WorkingSetBytes / 1024.0 / 1024.0);
        PerfCpu.Text = $"{info.CpuPercent:0.0}%";
        PerfMem.Text = Fmt.Bytes(info.WorkingSetBytes);
        CpuChart.SetData(_cpu.ToArray(), 100);
        MemChart.SetData(_mem.ToArray(), Math.Max(_mem.Max(), 1));
    }

    private void Tabs_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        int i = sender.Items.IndexOf(sender.SelectedItem);
        OverviewPanel.Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed;
        PerfPanel.Visibility = i == 1 ? Visibility.Visible : Visibility.Collapsed;
        ModulesPanel.Visibility = i == 2 ? Visibility.Visible : Visibility.Collapsed;

        if (i == 2 && !_modulesLoaded)
        {
            _modulesLoaded = true;
            _ = LoadModulesAsync();
        }
    }

    private async Task LoadModulesAsync()
    {
        var mods = await Task.Run(() => ProcessDetailsProvider.Modules(_pid));
        Modules.ItemsSource = mods.Count == 0
            ? new List<KV> { new("No modules", "This process is protected or has exited.") }
            : mods.OrderBy(System.IO.Path.GetFileName, StringComparer.OrdinalIgnoreCase)
                  .Select(m => new KV(System.IO.Path.GetFileName(m), m)).ToList();
    }

    private static Border Chip(string text, Brush fg) => new()
    {
        Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
        CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 1, 7, 2),
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = fg },
    };

    public void Dispose() => _subscription?.Dispose();
}
