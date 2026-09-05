using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PowerX.App.Services;
using PowerX.Core.Diagnostics;

namespace PowerX.App.Views;

public sealed record TempVm(string Name, string Category, string Detail, Visibility DetailVisible, string Value, Brush ValueBrush);

public sealed partial class TemperaturePage : Page
{
    private IDisposable? _subscription;
    private int _tick;
    private const int RefreshEveryTicks = 8;   // temperatures don't need a 1-second cadence

    public TemperaturePage()
    {
        InitializeComponent();
        _ = LoadAsync();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e) => _subscription = TelemetryHub.Instance.Subscribe(OnTick);
    protected override void OnNavigatedFrom(NavigationEventArgs e) => _subscription?.Dispose();

    private void OnTick(object? sender, EventArgs e)
    {
        if (_tick++ % RefreshEveryTicks != 0) return;
        _ = LoadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private bool _loading;

    private async Task LoadAsync()
    {
        if (_loading) return;
        _loading = true;
        RefreshButton.IsEnabled = false;
        try
        {
            var report = DemoData.Active ? DemoData.ThermalReport() : await Task.Run(ThermalInfo.Read);
            Render(report);
        }
        catch (Exception ex)
        {
            App.Log("Temperatures.Load", ex);
            Summary.Text = "Could not read temperatures: " + ex.Message;
        }
        finally
        {
            _loading = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void Render(ThermalReport report)
    {
        UnsupportedCard.Visibility = report.AcpiThermalZoneSupported ? Visibility.Collapsed : Visibility.Visible;

        Summary.Text = report.Readings.Count == 0
            ? "Nothing readable on this machine, see the note below."
            : $"{report.Readings.Count} reading{(report.Readings.Count == 1 ? "" : "s")} from what Windows exposes: "
              + (report.AcpiThermalZoneSupported ? "ACPI thermal zones and " : "") + "per-disk sensors.";

        var normal = (Brush)Application.Current.Resources["TextFillColorPrimaryBrush"];
        var caution = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
        var critical = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];

        List.ItemsSource = report.Readings.Select(r => new TempVm(
            r.Name,
            r.Category == ThermalCategory.Disk ? "Disk" : "System",
            r.Detail,
            string.IsNullOrEmpty(r.Detail) ? Visibility.Collapsed : Visibility.Visible,
            $"{r.TemperatureC:0.#} °C",
            r.TemperatureC switch
            {
                >= 75 => critical,
                >= 55 => caution,
                _ => normal,
            })).ToList();
    }
}
