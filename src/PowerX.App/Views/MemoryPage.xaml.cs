using System.Collections.ObjectModel;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using PowerX.App.Services;
using PowerX.Core.Telemetry;

namespace PowerX.App.Views;

public sealed partial class MemoryPage : Page
{
    private static readonly string[] Rows =
        ["In use", "Available", "Cached", "Committed", "Commit limit", "Paged pool", "Non-paged pool"];

    private readonly ObservableCollection<NameValueVm> _breakdown = [];
    private IDisposable? _subscription;

    public MemoryPage()
    {
        InitializeComponent();
        Spark.Accent = Color.FromArgb(0xFF, 0xA9, 0x6B, 0xF0);
        Spark.MaxLabel = "100%";
        foreach (var r in Rows) _breakdown.Add(new NameValueVm { Name = r });
        Breakdown.ItemsSource = _breakdown;
        PageLayout.CenterCap(this, Root, 1120);
        _ = LoadHardwareAsync();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _subscription = TelemetryHub.Instance.Subscribe(OnTick);
    protected override void OnNavigatedFrom(NavigationEventArgs e) => _subscription?.Dispose();

    private async Task LoadHardwareAsync()
    {
        try
        {
            var hw = await TelemetryHub.Instance.GetMemoryHardwareAsync();
            string speed = hw.EffectiveSpeedMtps > 0 ? $"{hw.EffectiveSpeedMtps} MT/s" : "unknown speed";
            HwSummary.Text = $"{Fmt.Bytes(hw.TotalPhysicalBytes)} {hw.DominantType} · {speed} · {hw.SlotsUsed}/{hw.SlotsTotal} slots used";

            if (hw.Modules.Count > 0)
            {
                var items = new List<NameValueVm>();
                foreach (var m in hw.Modules)
                {
                    string rated = m.SpeedMtps > 0 && m.SpeedMtps != m.ConfiguredSpeedMtps ? $" (rated {m.SpeedMtps})" : "";
                    string maker = string.Join(" ", new[] { m.Manufacturer, m.PartNumber }.Where(s => !string.IsNullOrWhiteSpace(s)));
                    items.Add(new NameValueVm
                    {
                        Name = m.Slot,
                        Value = $"{Fmt.Bytes(m.CapacityBytes)} · {m.Type} · {(m.ConfiguredSpeedMtps > 0 ? m.ConfiguredSpeedMtps : m.SpeedMtps)} MT/s{rated} · {m.FormFactor}"
                                + (maker.Length > 0 ? $"\n{maker}" : ""),
                    });
                }
                if (hw.MaxCapacityBytes > 0)
                    items.Add(new NameValueVm { Name = "Max capacity", Value = Fmt.Bytes(hw.MaxCapacityBytes) });
                Modules.ItemsSource = items;
                ModulesCard.Visibility = Visibility.Visible;
                AboutCard.Visibility = Visibility.Collapsed;
            }
        }
        catch (Exception)
        {
            HwSummary.Text = "Hardware details unavailable";
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (TelemetryHub.Instance.LastMemory?.Value is not { } m) return;

        
        Gauge.Value = m.UsedPercent;
        Gauge.ValueText = $"{m.UsedPercent:0}%";
        Sub.Text = $"{Fmt.Bytes(m.InUsePhysical)}\nof {Fmt.Bytes(m.TotalPhysical)}";
        Spark.SetData(TelemetryHub.Instance.MemHistory.ToArray(), 100);

        _breakdown[0].Value = Fmt.Bytes(m.InUsePhysical);
        _breakdown[1].Value = Fmt.Bytes(m.AvailablePhysical);
        _breakdown[2].Value = Fmt.Bytes(m.CachedApprox);
        _breakdown[3].Value = $"{Fmt.Bytes(m.CommitTotal)}  ({m.CommitPercent:0}%)";
        _breakdown[4].Value = Fmt.Bytes(m.CommitLimit);
        _breakdown[5].Value = Fmt.Bytes(m.PagedPool);
        _breakdown[6].Value = Fmt.Bytes(m.NonPagedPool);
    }
}
