using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PowerX.App.Services;
using PowerX.Core.Telemetry;

namespace PowerX.App.Views;

public sealed partial class GpuPage : Page
{
    private readonly ObservableCollection<CoreVm> _engines = [];
    private IDisposable? _subscription;
    private ulong _vramTotal;
    private readonly Dictionary<long, AdapterCardRefs> _adapterCards = [];
    private readonly Dictionary<long, ulong> _adapterVramTotal = [];
    private readonly List<long?> _pickerLuids = [];   // null = Combined; parallel to AdapterPicker's items
    private long? _selectedLuid;                       // null = Combined
    private GpuMetrics? _lastGpu;

    private sealed record AdapterCardRefs(Controls.LoadBar Bar, TextBlock Percent, TextBlock Vram, TextBlock Engine);

    public GpuPage()
    {
        InitializeComponent();
        Spark.Accent = Color.FromArgb(0xFF, 0x3F, 0xB9, 0x50);
        Spark.MaxLabel = "100%";
        Engines.ItemsSource = _engines;
        PageLayout.CenterCap(this, Root, 1120);

        var adapters = TelemetryHub.Instance.GpuAdapters;
        var primary = adapters.OrderByDescending(a => a.DedicatedMemoryTotal).FirstOrDefault();
        AdapterName.Text = adapters.Count > 1
            ? $"{adapters.Count} GPUs: {string.Join(", ", adapters.Select(a => a.Name))}"
            : primary?.Name ?? "Display adapter";
        // The hero card's VRAM bar is a combined used/total across every adapter (matching how
        // the blended utilisation number above it works), so it needs every adapter's capacity,
        // not just the biggest one's.
        _vramTotal = adapters.Aggregate(0UL, (sum, a) => sum + a.DedicatedMemoryTotal);
        foreach (var a in adapters.Where(a => a.Luid != 0)) _adapterVramTotal[a.Luid] = a.DedicatedMemoryTotal;

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

        if (adapters.Count > 1)
        {
            BuildAdapterCards(adapters);
            BuildPicker(adapters, primary);
        }
    }

    // A picker to choose which GPU's numbers drive the hero gauge/chart/engine list — defaults to
    // the same adapter the page always showed before per-adapter data existed (the one with the
    // most VRAM), not "Combined", so nothing changes for someone who never touches it.
    private void BuildPicker(IReadOnlyList<GpuAdapter> adapters, GpuAdapter? primary)
    {
        var items = new List<string> { "Combined" };
        _pickerLuids.Add(null);
        int defaultIndex = 0;
        foreach (var a in adapters.Where(a => a.Luid != 0))
        {
            items.Add(a.Name);
            _pickerLuids.Add(a.Luid);
            if (primary is not null && a.Luid == primary.Luid) defaultIndex = items.Count - 1;
        }
        AdapterPicker.ItemsSource = items;
        AdapterPicker.Visibility = Visibility.Visible;
        AdapterPicker.SelectedIndex = defaultIndex;
        _selectedLuid = _pickerLuids[defaultIndex];
    }

    private void AdapterPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int i = AdapterPicker.SelectedIndex;
        _selectedLuid = i >= 0 && i < _pickerLuids.Count ? _pickerLuids[i] : null;
        if (_lastGpu is { } g) RenderHero(g);
    }

    // GPUs with more than one real adapter (integrated + discrete is the common case) get one
    // card per GPU below the hero — an always-visible overview regardless of which one the
    // picker above has selected.
    private void BuildAdapterCards(IReadOnlyList<GpuAdapter> adapters)
    {
        AdaptersSection.Visibility = Visibility.Visible;
        AdapterCards.Children.Clear();
        _adapterCards.Clear();

        foreach (var a in adapters.Where(a => a.Luid != 0))
        {
            var body = new StackPanel { Spacing = 8 };
            body.Children.Add(new TextBlock
            {
                Text = a.Name, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                TextTrimming = TextTrimming.CharacterEllipsis,
            });

            var loadRow = new Grid { ColumnSpacing = 10 };
            loadRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            loadRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bar = new Controls.LoadBar { VerticalAlignment = VerticalAlignment.Center };
            var pct = new TextBlock { Style = (Style)Application.Current.Resources["MonoStyle"], VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(pct, 1);
            loadRow.Children.Add(bar);
            loadRow.Children.Add(pct);
            body.Children.Add(loadRow);

            var vram = new TextBlock { FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
            var engine = new TextBlock { FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"] };
            body.Children.Add(vram);
            body.Children.Add(engine);

            AdapterCards.Children.Add(new Border { Style = (Style)Application.Current.Resources["CardStyle"], Child = body });
            _adapterCards[a.Luid] = new AdapterCardRefs(bar, pct, vram, engine);
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
        _lastGpu = g;
        RenderHero(g);

        foreach (var au in g.Adapters)
        {
            if (!_adapterCards.TryGetValue(au.Luid, out var refs)) continue;
            refs.Bar.Value = au.UtilizationPercent;
            refs.Percent.Text = $"{au.UtilizationPercent:0}%";
            refs.Vram.Text = au.DedicatedMemoryTotal > 0
                ? $"VRAM {Fmt.Bytes(au.DedicatedMemoryUsed)} / {Fmt.Bytes(au.DedicatedMemoryTotal)}"
                : $"VRAM {Fmt.Bytes(au.DedicatedMemoryUsed)}";
            refs.Engine.Text = au.Engines.Count > 0 ? $"busiest: {au.Engines[0].Engine}" : "idle";
        }
    }

    // Drives the hero gauge/chart/engine list/VRAM bar from either the blended totals or one
    // specific adapter, depending on what AdapterPicker has selected.
    private void RenderHero(GpuMetrics g)
    {
        var selected = _selectedLuid is { } luid ? g.Adapters.FirstOrDefault(a => a.Luid == luid) : null;
        bool showingOne = selected is not null;

        double util = showingOne ? selected!.UtilizationPercent : g.UtilizationPercent;
        var engines = showingOne ? selected!.Engines : g.Engines;
        ulong used = showingOne ? selected!.DedicatedMemoryUsed : g.DedicatedMemoryUsed;
        ulong shared = showingOne ? selected!.SharedMemoryUsed : g.SharedMemoryUsed;
        ulong total = showingOne ? _adapterVramTotal.GetValueOrDefault(selected!.Luid) : _vramTotal;
        var history = showingOne && TelemetryHub.Instance.GpuAdapterHistory.TryGetValue(selected!.Luid, out var r)
            ? r : TelemetryHub.Instance.GpuHistory;

        Gauge.Value = util;
        Gauge.ValueText = $"{util:0}%";
        string busiest = engines.Count > 0 ? $"busiest: {engines[0].Engine}" : "";
        OverallSub.Text = !showingOne && g.Adapters.Count > 1 ? $"{busiest}  ·  combined across {g.Adapters.Count} GPUs" : busiest;
        Spark.SetData(history.ToArray(), 100);

        SyncEngines(engines);

        VramText.Text = total > 0 ? $"{Fmt.Bytes(used)} / {Fmt.Bytes(total)}" : Fmt.Bytes(used);
        VramBar.Value = total > 0 ? Math.Clamp(100.0 * used / total, 0, 100) : 0;
        SharedText.Text = $"Shared memory in use: {Fmt.Bytes(shared)}";
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
