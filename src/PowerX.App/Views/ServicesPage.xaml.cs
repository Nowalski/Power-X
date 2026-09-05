using System.Collections.ObjectModel;
using System.Diagnostics;
using System.ServiceProcess;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerX.Core.Services;
using CoreServices = PowerX.Core.Services.ServiceProvider;

namespace PowerX.App.Views;

public sealed partial class ServicesPage : Page
{
    private readonly ObservableCollection<ServiceVm> _view = [];
    private List<ServiceVm> _all = [];
    private string _filter = "";
    private int _show;
    private bool _loaded;

    public ServicesPage()
    {
        InitializeComponent();
        List.ItemsSource = _view;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        _loaded = false;
        IReadOnlyList<ServiceEntry> entries;
        try
        {
            entries = await Task.Run(() => CoreServices.Enumerate());
        }
        catch (Exception ex)
        {
            App.Log("Services.Enumerate", ex);
            Summary.Text = "Could not read services: " + ex.Message;
            return;
        }
        _all = entries.Select(e => new ServiceVm { Entry = e }).ToList();
        Summary.Text = $"{_all.Count} services · {_all.Count(s => s.IsRunning)} running";
        _loaded = true;
        Render();
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_loaded) return;
        _filter = Filter.Text.Trim();
        Render();
    }

    private void Show_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_loaded) return;
        _show = ShowBox.SelectedIndex;
        Render();
    }

    private void Render()
    {
        IEnumerable<ServiceVm> shown = _all;
        shown = _show switch
        {
            1 => shown.Where(s => s.IsRunning),
            2 => shown.Where(s => !s.IsRunning),
            3 => shown.Where(s => s.Entry.StartMode is ServiceStartMode2.Automatic or ServiceStartMode2.AutomaticDelayed),
            _ => shown,
        };
        if (_filter.Length > 0)
            shown = shown.Where(s =>
                s.DisplayName.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                s.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                s.Description.Contains(_filter, StringComparison.OrdinalIgnoreCase));

        _view.Clear();
        foreach (var vm in shown.Take(500)) _view.Add(vm);
    }

    // ---------------------------------------------------------------- actions

    private ServiceVm? Vm(object sender) =>
        (sender as FrameworkElement)?.Tag is string name ? _all.FirstOrDefault(s => s.Name == name) : null;

    private async void StartStop_Click(object sender, RoutedEventArgs e)
    {
        if (Vm(sender) is not { } vm) return;
        bool starting = !vm.IsRunning;

        if (!starting && vm.IsCritical &&
            !await Confirm($"Stop {vm.DisplayName}?", "This is a core Windows service. Stopping it can make the system unstable until you restart."))
            return;

        await Run(vm, () => starting ? CoreServices.Start(vm.Name) : CoreServices.Stop(vm.Name), starting ? "start" : "stop");
    }

    private async void Restart_Click(object sender, RoutedEventArgs e)
    {
        if (Vm(sender) is not { } vm) return;
        if (vm.IsCritical && !await Confirm($"Restart {vm.DisplayName}?", "This is a core Windows service.")) return;
        await Run(vm, () => CoreServices.Restart(vm.Name), "restart");
    }

    private async void ModeAuto_Click(object s, RoutedEventArgs e) => await SetMode(s, ServiceStartMode2.Automatic);
    private async void ModeDelayed_Click(object s, RoutedEventArgs e) => await SetMode(s, ServiceStartMode2.AutomaticDelayed);
    private async void ModeManual_Click(object s, RoutedEventArgs e) => await SetMode(s, ServiceStartMode2.Manual);
    private async void ModeDisabled_Click(object s, RoutedEventArgs e) => await SetMode(s, ServiceStartMode2.Disabled);

    private async Task SetMode(object sender, ServiceStartMode2 mode)
    {
        if (Vm(sender) is not { } vm) return;
        if (mode == ServiceStartMode2.Disabled && vm.IsCritical &&
            !await Confirm($"Disable {vm.DisplayName}?", "This is a core Windows service. Disabling it may break boot or sign-in."))
            return;

        await Run(vm, () => CoreServices.SetStartMode(vm.Name, mode), "change start type");
    }

    private void OpenMsc_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo("services.msc") { UseShellExecute = true }); }
        catch (Exception ex) { App.Log("services.msc", ex); }
    }

    private async Task Run(ServiceVm vm, Func<PowerX.Core.Processes.ActionResult> op, string verb)
    {
        vm.SetBusy(true);
        var result = await Task.Run(op);
        // re-read this one service
        var fresh = await Task.Run(() => CoreServices.Enumerate().FirstOrDefault(x => x.Name == vm.Name));
        if (fresh is not null) vm.Refresh(fresh);
        vm.SetBusy(false);
        Summary.Text = $"{_all.Count} services · {_all.Count(s => s.IsRunning)} running";

        if (!result.Success)
            await new ContentDialog
            {
                Title = $"Could not {verb} the service",
                Content = result.Message ?? "Unknown error.",
                CloseButtonText = "Close", XamlRoot = XamlRoot,
            }.ShowAsync();
    }

    private async Task<bool> Confirm(string title, string body) => await new ContentDialog
    {
        Title = title, Content = body,
        PrimaryButtonText = "Continue", CloseButtonText = "Cancel",
        DefaultButton = ContentDialogButton.Close, XamlRoot = XamlRoot,
    }.ShowAsync() == ContentDialogResult.Primary;
}
