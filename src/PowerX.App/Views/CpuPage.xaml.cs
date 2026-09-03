using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PowerX.App.Services;
using PowerX.Core.Telemetry;

namespace PowerX.App.Views;

public sealed partial class CpuPage : Page
{
    private readonly ObservableCollection<CoreVm> _cores = [];
    private IDisposable? _subscription;

    public CpuPage()
    {
        InitializeComponent();
        Spark.Accent = Color.FromArgb(0xFF, 0x4C, 0x8B, 0xF5);
        Spark.MaxLabel = "100%";
        Cores.ItemsSource = _cores;
        PageLayout.CenterCap(this, Root, 1120);

        var ci = TelemetryHub.Instance.CpuInfo;
        if (ci is not null)
        {
            CpuName.Text = ci.Name;
            string caches = string.Join("   ",
                new[] { ci.L1, ci.L2, ci.L3 }
                    .Where(c => c is not null)
                    .Select(c => $"L{c!.Level} {Fmt.Bytes(c.TotalBytes)}"));

            Spec.ItemsSource = new List<NameValueVm>
            {
                NV("Vendor", ci.Vendor),
                NV("Base speed", $"{ci.BaseClockMhz / 1000.0:0.00} GHz"),
                NV("Max speed", ci.MaxClockMhz > 0 ? $"{ci.MaxClockMhz / 1000.0:0.00} GHz" : "—"),
                NV("Sockets", ci.Packages.ToString()),
                NV("Cores", ci.PhysicalCores.ToString()),
                NV("Logical processors", ci.LogicalProcessors.ToString()),
                NV("Simultaneous MT", ci.HyperThreading ? "Enabled" : "Disabled"),
                NV("Hybrid cores", ci.IsHybrid ? $"{ci.PerformanceCores} performance + {ci.EfficiencyCores} efficiency" : "No"),
                NV("Virtualization", ci.VirtualizationFirmwareEnabled ? "Enabled in firmware" : "Not enabled in firmware"),
                NV("SLAT / nested paging", ci.SecondLevelAddressTranslation ? "Supported" : "Not supported"),
                NV("Cache", string.IsNullOrEmpty(caches) ? "—" : caches),
            };
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _subscription = TelemetryHub.Instance.Subscribe(OnTick);
    protected override void OnNavigatedFrom(NavigationEventArgs e) => _subscription?.Dispose();

    private void OnTick(object? sender, EventArgs e)
    {
        var hub = TelemetryHub.Instance;
        if (hub.LastCpu?.Value is not { } cpu) return;

        Gauge.Value = cpu.TotalUsagePercent;
        Gauge.ValueText = $"{cpu.TotalUsagePercent:0}%";
        OverallSub.Text = $"kernel {cpu.KernelUsagePercent:0.0}%";
        Detail.Text = $"{cpu.ProcessCount} processes  ·  {cpu.ThreadCount} threads  ·  {cpu.HandleCount:N0} handles  ·  up {Fmt.Duration(cpu.Uptime)}";
        Spark.SetData(hub.CpuHistory.ToArray(), 100);

        var per = cpu.PerLogicalProcessor;
        while (_cores.Count < per.Count) _cores.Add(new CoreVm(_cores.Count));
        while (_cores.Count > per.Count) _cores.RemoveAt(_cores.Count - 1);
        for (int i = 0; i < per.Count; i++) _cores[i].Usage = per[i];
    }

    private static NameValueVm NV(string n, string v) => new() { Name = n, Value = v };
}
