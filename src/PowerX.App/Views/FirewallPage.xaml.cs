using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PowerX.Core.Diagnostics;

namespace PowerX.App.Views;

public sealed record FirewallVm(string Name, string Detail, string Tag, Brush TagBrush, Visibility FlagVisible);

public sealed partial class FirewallPage : Page
{
    private IReadOnlyList<FirewallRule> _all = [];
    private string _filter = "";
    private int _dir;
    private bool _reviewOnly;
    private bool _loaded;

    public FirewallPage()
    {
        InitializeComponent();
        _ = LoadAsync();
    }

    private void Filter_Changed(object sender, TextChangedEventArgs e) { if (_loaded) { _filter = Filter.Text.Trim(); Render(); } }
    private void Filter_Toggled(object sender, SelectionChangedEventArgs e) { if (_loaded) { _dir = DirBox.SelectedIndex; Render(); } }
    private void Toggle_Click(object sender, RoutedEventArgs e) { if (_loaded) { _reviewOnly = ReviewOnly.IsChecked == true; Render(); } }

    private void OpenFw_Click(object sender, RoutedEventArgs e)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("wf.msc") { UseShellExecute = true }); }
        catch (Exception ex) { App.Log("Firewall.Open", ex); }
    }

    private async Task LoadAsync()
    {
        Summary.Text = "Reading firewall rules...";
        FirewallProfileState state;
        try
        {
            if (Services.DemoData.Active)
            {
                (_all, state) = (Services.DemoData.FirewallRules(), new FirewallProfileState(true, true, true));
            }
            else
            {
                (_all, state) = await Task.Run(() => (FirewallRules.Rules(), FirewallRules.ProfileState()));
            }
        }
        catch (Exception ex)
        {
            App.Log("Firewall.Load", ex);
            Summary.Text = "Could not read the firewall: " + ex.Message;
            return;
        }

        _loaded = true;
        OffBar.IsOpen = state.AnyOff;
        if (state.AnyOff)
        {
            var off = new List<string>();
            if (!state.DomainOn) off.Add("Domain");
            if (!state.PrivateOn) off.Add("Private");
            if (!state.PublicOn) off.Add("Public");
            OffBar.Title = "Firewall is off for the " + string.Join(", ", off) + " profile" + (off.Count > 1 ? "s" : "");
            OffBar.Message = "Turn it back on in Windows Security unless you have a deliberate reason. PowerX does not change firewall settings.";
        }

        Render();
    }

    private void Render()
    {
        int review = _all.Count(r => r.WorthReviewing);
        Summary.Text = $"{_all.Count} rules, {_all.Count(r => r.Enabled)} enabled."
                     + (review > 0 ? $"  {review} broad inbound-allow rule(s) worth a look." : "")
                     + "  Read-only; PowerX does not change firewall rules.";

        var caution = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
        var ok = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
        var crit = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];

        IEnumerable<FirewallRule> shown = _all;
        if (_dir == 1) shown = shown.Where(r => r.Direction == FwDirection.In);
        if (_dir == 2) shown = shown.Where(r => r.Direction == FwDirection.Out);
        if (_reviewOnly) shown = shown.Where(r => r.WorthReviewing);
        if (_filter.Length > 0)
            shown = shown.Where(r =>
                r.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                r.Program.Contains(_filter, StringComparison.OrdinalIgnoreCase) ||
                r.Grouping.Contains(_filter, StringComparison.OrdinalIgnoreCase));

        List.ItemsSource = shown.Take(600).Select(r =>
        {
            var profiles = new List<string>();
            if (r.Domain) profiles.Add("domain");
            if (r.Private) profiles.Add("private");
            if (r.Public) profiles.Add("public");

            string prog = string.IsNullOrWhiteSpace(r.Program)
                ? (string.IsNullOrWhiteSpace(r.Service) ? "any program" : "service: " + r.Service)
                : System.IO.Path.GetFileName(r.Program);
            string ports = r.Direction == FwDirection.In
                ? (string.IsNullOrWhiteSpace(r.LocalPorts) ? "" : $"local {r.LocalPorts}")
                : (string.IsNullOrWhiteSpace(r.RemotePorts) ? "" : $"remote {r.RemotePorts}");

            string detail = string.Join("  ", new[] { prog, r.Protocol, ports, string.Join("/", profiles), r.Enabled ? null : "disabled" }
                .Where(x => !string.IsNullOrEmpty(x)));

            string tag = $"{(r.Direction == FwDirection.In ? "IN" : "OUT")} {(r.Action == FwAction.Allow ? "allow" : "block")}";
            var tagBrush = r.Action == FwAction.Allow
                ? (r.Direction == FwDirection.In ? caution : ok)
                : ok;

            return new FirewallVm(r.Name, detail, tag, tagBrush, r.WorthReviewing ? Visibility.Visible : Visibility.Collapsed);
        }).ToList();
    }
}
