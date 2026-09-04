using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerX.Core.Diagnostics;

namespace PowerX.App.Views;

public sealed record DriverVm(string Device, string Meta, string Version, string Flag, Brush FlagBrush, Visibility FlagVisible);

public sealed partial class DriversPage : Page
{
    private IReadOnlyList<DriverEntry> _all = [];
    private string _filter = "";
    private bool _oldOnly;
    private bool _loaded;

    public DriversPage()
    {
        InitializeComponent();
        _ = LoadAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();

    private void Filter_Changed(object sender, TextChangedEventArgs e) { if (_loaded) { _filter = Filter.Text.Trim(); Render(); } }
    private void OldOnly_Click(object sender, RoutedEventArgs e) { if (_loaded) { _oldOnly = OldOnly.IsChecked == true; Render(); } }

    private async Task LoadAsync()
    {
        RefreshButton.IsEnabled = false;
        Summary.Text = "Reading the driver inventory...";
        try
        {
            _all = Services.DemoData.Active ? Services.DemoData.Drivers() : await DriverInventory.ReadAsync();
        }
        catch (Exception ex)
        {
            App.Log("Drivers.Load", ex);
            Summary.Text = "Could not read the driver inventory: " + ex.Message;
            return;
        }
        finally { RefreshButton.IsEnabled = true; }
        _loaded = true;
        Render();
    }

    private void Render()
    {
        int old = _all.Count(d => d.Age is DriverAge.Old or DriverAge.VeryOld);
        Summary.Text = $"{_all.Count} drivers, {_all.Count(d => !d.IsInbox)} from a third party."
                     + (old > 0 ? $"  {old} are three years old or more." : "  None look stale.")
                     + "  PowerX never installs a driver; check the vendor if one is flagged.";

        var caution = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
        var critical = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];

        IEnumerable<DriverEntry> shown = _all;
        if (_oldOnly) shown = shown.Where(d => d.Age is DriverAge.Old or DriverAge.VeryOld);
        if (_filter.Length > 0)
            shown = shown.Where(d =>
                d.Device.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                d.Provider.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                d.DeviceClass.Contains(_filter, StringComparison.OrdinalIgnoreCase));

        List.ItemsSource = shown.Select(d =>
        {
            string meta = string.Join("  ", new[]
            {
                string.IsNullOrEmpty(d.Provider) ? null : d.Provider,
                string.IsNullOrEmpty(d.DeviceClass) ? null : d.DeviceClass.ToLowerInvariant(),
                d.Date is { } dt ? dt.LocalDateTime.ToString("MMM yyyy") : null,
                d.Signed ? null : "unsigned",
            }.Where(x => x is not null));

            var (flag, brush, vis) = d.Age switch
            {
                DriverAge.VeryOld => ($"{d.AgeYears} years old", critical, Visibility.Visible),
                DriverAge.Old => ($"{d.AgeYears} years old", caution, Visibility.Visible),
                _ => ("", caution, Visibility.Collapsed),
            };
            if (!d.Signed) { flag = "unsigned"; brush = critical; vis = Visibility.Visible; }

            return new DriverVm(d.Device, meta, d.Version, flag, brush, vis);
        }).ToList();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            nint hwnd = App.Window is { } w ? WinRT.Interop.WindowNative.GetWindowHandle(w) : 0;
            string? path = Services.NativeFileDialog.SaveFile(hwnd, "powerx-drivers.csv", "csv", "Save the driver list");
            if (string.IsNullOrEmpty(path)) return;

            Services.CsvExport.Write(path,
                ["Device", "Provider", "DeviceClass", "Version", "Date", "Signed", "AgeYears"],
                _all.Select(d => (IReadOnlyList<string>)[
                    d.Device, d.Provider, d.DeviceClass, d.Version,
                    d.Date?.ToString("yyyy-MM-dd") ?? "", d.Signed ? "yes" : "no", d.AgeYears.ToString(),
                ]));
        }
        catch (Exception ex) { App.Log("Drivers.Export", ex); }
    }
}
