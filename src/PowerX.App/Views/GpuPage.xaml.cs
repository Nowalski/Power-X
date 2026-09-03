using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PowerX.App.Services;
using PowerX.Core.Telemetry;

namespace PowerX.App.Views;

public sealed partial class GpuPage : Page
{
    private readonly ObservableCollection<CoreVm> _engines = [];
    private IDisposable? _subscription;
    private ulong _vramTotal;

    public GpuPage()
    {
        InitializeComponent();
        Spark.Accent = Color.FromArgb(0xFF, 0x3F, 0xB9, 0x50);
        Spark.MaxLabel = "100%";
        Engines.ItemsSource = _engines;
        PageLayout.CenterCap(this, Root, 1120);

        var adapters = TelemetryHub.Instance.GpuAdapters;
        var primary = adapters.OrderByDescending(a => a.DedicatedMemoryTotal).FirstOrDefault();
        AdapterName.Text = primary?.Name ?? "Display adapter";
        _vramTotal = primary?.DedicatedMemoryTotal ?? 0;

        if (primary is not null)
        {
            var spec = new List<NameValueVm>
            {
                NV("Driver version", primary.DriverVersion),
                NV("Dedicated memory", primary.DedicatedMemoryTotal > 0 ? Fmt.Bytes(primary.DedicatedMemoryTotal) : "—"),
            };
            if (primary.CurrentResolution.W > 0)
                spec.Add(NV("Current mode", $"{primary.CurrentResolution.W} × {primary.CurrentResolution.H} @ {primary.RefreshHz} Hz"));
            if (adapters.Count > 1)
                spec.Add(NV("Other adapters", string.Join(", ", adapters.Where(a => a != primary).Select(a => a.Name))));
            Spec.ItemsSource = spec;
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _subscription = TelemetryHub.Instance.Subscribe(OnTick);
    protected override void OnNavigatedFrom(NavigationEventArgs e) => _subscription?.Dispose();

    private void OnTick(object? sender, EventArgs e)
    {
        var res = TelemetryHub.Instance.LastGpu;
        if (res is null) return;

        if (!res.HasValue)
        {
            UnavailableCard.Visibility = Visibility.Visible;
            UnavailableText.Text = "GPU performance counters are not available on this system. " + (res.Detail ?? "");
            Gauge.ValueText = "n/a";
            OverallSub.Text = "";
            return;
        }
        UnavailableCard.Visibility = Visibility.Collapsed;

        var g = res.Value!;
        
        Gauge.Value = g.UtilizationPercent;
        Gauge.ValueText = $"{g.UtilizationPercent:0}%";
        OverallSub.Text = g.Engines.Count > 0 ? $"busiest: {g.Engines[0].Engine}" : "";
        Spark.SetData(TelemetryHub.Instance.GpuHistory.ToArray(), 100);

        SyncEngines(g.Engines);

        ulong used = g.DedicatedMemoryUsed;
        VramText.Text = _vramTotal > 0 ? $"{Fmt.Bytes(used)} / {Fmt.Bytes(_vramTotal)}" : Fmt.Bytes(used);
        VramBar.Value = _vramTotal > 0 ? Math.Clamp(100.0 * used / _vramTotal, 0, 100) : 0;
        SharedText.Text = $"Shared memory in use: {Fmt.Bytes(g.SharedMemoryUsed)}";
    }

    // GPUs expose ~20 engine types, most permanently idle (OFA, JPEG decode, timers…). Show the
    // ones people care about plus anything currently active — never more than ten.
    private static readonly string[] CoreEngines = ["3D", "Copy", "Compute", "Video Decode", "Video Encode", "Graphics"];

    private void SyncEngines(IReadOnlyList<GpuEngineLoad> engines)
    {
        var shown = engines
            .Where(e => e.Percent >= 0.3 || CoreEngines.Any(c => e.Engine.StartsWith(c, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(e => e.Percent)
            .ThenBy(e => e.Engine, StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        while (_engines.Count < shown.Count) _engines.Add(new CoreVm(_engines.Count));
        while (_engines.Count > shown.Count) _engines.RemoveAt(_engines.Count - 1);
        for (int i = 0; i < shown.Count; i++)
        {
            _engines[i].SetLabel(shown[i].Engine);
            _engines[i].Usage = shown[i].Percent;
        }
    }

    private static NameValueVm NV(string n, string v) => new() { Name = n, Value = v };
}
