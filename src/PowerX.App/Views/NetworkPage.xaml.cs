using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PowerX.App.Services;
using PowerX.Core.Telemetry;

namespace PowerX.App.Views;

public sealed record ConnVm(string Name, string Proto, string Local, string Remote, string State);

public sealed partial class NetworkPage : Page
{
    private IDisposable? _subscription;
    private int _tick;
    private string _connFilter = "";
    private IReadOnlyList<NetworkConnection> _connections = [];
    private bool _onPage;
    private bool _refreshingConnections;

    public NetworkPage()
    {
        InitializeComponent();
        DownSpark.Accent = Color.FromArgb(0xFF, 0x33, 0xB0, 0xA6);
        UpSpark.Accent = Color.FromArgb(0xFF, 0xE0, 0x9B, 0x3A);
        PageLayout.CenterCap(this, Root, 1500);
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        _onPage = true;
        _subscription = TelemetryHub.Instance.Subscribe(OnTick);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        _onPage = false;
        _subscription?.Dispose();
        _diagCts?.Cancel();   // RunDiag's finally disposes it once the op unwinds
    }

    // ---------------------------------------------------------------- diagnostics

    private CancellationTokenSource? _diagCts;

    private async void Ping_Click(object s, RoutedEventArgs e) =>
        await RunDiag(ct => PowerX.Core.Diagnostics.NetTools.PingAsync(DiagHost.Text.Trim(), 6, DiagLine, ct));

    private async void Trace_Click(object s, RoutedEventArgs e) =>
        await RunDiag(ct => PowerX.Core.Diagnostics.NetTools.TracerouteAsync(DiagHost.Text.Trim(), DiagLine, ct));

    private async void Dns_Click(object s, RoutedEventArgs e) =>
        await RunDiag(ct => PowerX.Core.Diagnostics.NetTools.DnsLookupAsync(DiagHost.Text.Trim(), DiagLine, ct));

    private void DiagStop_Click(object s, RoutedEventArgs e) => _diagCts?.Cancel();

    private void DiagLine(string line) =>
        DispatcherQueue.TryEnqueue(() =>
        {
            DiagOut.Text += line + "\n";
            DiagOut.Select(DiagOut.Text.Length, 0);
        });

    private async Task RunDiag(Func<CancellationToken, Task> op)
    {
        if (_diagCts is not null) return;
        if (string.IsNullOrWhiteSpace(DiagHost.Text)) return;
        _diagCts = new CancellationTokenSource();
        DiagOut.Text = "";
        SetDiagBusy(true);
        var cts = _diagCts;
        try { await op(cts.Token); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { DiagLine($"error: {ex.Message}"); }
        finally
        {
            SetDiagBusy(false);
            _diagCts = null;
            cts.Dispose();
        }
    }

    private void SetDiagBusy(bool busy)
    {
        PingButton.IsEnabled = TraceButton.IsEnabled = DnsButton.IsEnabled = DiagHost.IsEnabled = !busy;
        DiagStop.IsEnabled = busy;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (TelemetryHub.Instance.LastNetwork?.Value is not { } net) return;

        var down = TelemetryHub.Instance.NetDownHistory;
        var up = TelemetryHub.Instance.NetUpHistory;
        double scale = Math.Max(down.Max(), Math.Max(up.Max(), 64 * 1024));
        DownValue.Text = Fmt.Rate(net.TotalReceiveBytesPerSec);
        UpValue.Text = Fmt.Rate(net.TotalSendBytesPerSec);
        DownSpark.SetData(down.ToArray(), scale);
        UpSpark.SetData(up.ToArray(), scale);

        if (_tick++ % 3 != 0) return;
        RebuildInterfaces(net);
        _ = RefreshConnectionsAsync();
    }

    private async Task RefreshConnectionsAsync()
    {
        if (_refreshingConnections) return;   // don't stack up if a scan runs long
        _refreshingConnections = true;
        try
        {
            var names = TelemetryHub.Instance.LastProcesses?.Processes
                .GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First().Name)
                ?? new Dictionary<int, string>();
            var conns = await Task.Run(() => ConnectionProvider.Enumerate(names));
            if (!_onPage) return;             // navigated away while the scan ran — drop the result
            _connections = conns;
            RenderConnections();
        }
        finally
        {
            _refreshingConnections = false;
        }
    }

    private void ConnFilter_Changed(object sender, TextChangedEventArgs e)
    {
        _connFilter = ConnFilter.Text.Trim();
        RenderConnections();
    }

    private void RenderConnections()
    {
        IEnumerable<NetworkConnection> src = _connections;
        if (_connFilter.Length > 0)
            src = src.Where(c => c.ProcessName.Contains(_connFilter, StringComparison.OrdinalIgnoreCase)
                              || c.RemoteEndpoint.Contains(_connFilter, StringComparison.OrdinalIgnoreCase));
        Connections.ItemsSource = src.Take(300)
            .Select(c => new ConnVm(c.ProcessName, c.Protocol, c.LocalEndpoint, c.RemoteEndpoint, c.State))
            .ToList();
    }

    private void RebuildInterfaces(NetworkMetrics net)
    {
        Interfaces.Children.Clear();
        foreach (var i in net.Interfaces)
        {
            var rows = new StackPanel { Spacing = 3 };

            var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            header.Children.Add(new TextBlock
            {
                Text = i.Name,
                Style = (Style)Application.Current.Resources["SectionHeaderStyle"],
                VerticalAlignment = VerticalAlignment.Center,
            });
            header.Children.Add(Chip(i.IsUp ? "Connected" : "Down",
                i.IsUp ? (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"]
                       : (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]));
            header.Children.Add(Chip(i.Type, (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]));
            rows.Children.Add(header);

            rows.Children.Add(Line("Throughput", $"↓ {Fmt.Rate(i.ReceiveBytesPerSec)}    ↑ {Fmt.Rate(i.SendBytesPerSec)}"));
            if (i.LinkSpeedBps > 0)
                rows.Children.Add(Line("Link speed", $"{i.LinkSpeedBps / 1_000_000:N0} Mbps"));
            rows.Children.Add(Line("Total", $"received {Fmt.Bytes((ulong)i.TotalBytesReceived)} · sent {Fmt.Bytes((ulong)i.TotalBytesSent)}"));
            if (i.IpAddresses.Count > 0)
                rows.Children.Add(Line("IP address", string.Join("  ", i.IpAddresses)));
            if (i.Gateways.Count > 0)
                rows.Children.Add(Line("Gateway", string.Join("  ", i.Gateways)));
            if (i.DnsServers.Count > 0)
                rows.Children.Add(Line("DNS", string.Join("  ", i.DnsServers)));
            if (!string.IsNullOrEmpty(i.MacAddress))
                rows.Children.Add(Line("MAC", i.MacAddress));

            Interfaces.Children.Add(new Border
            {
                Style = (Style)Application.Current.Resources["CardStyle"],
                Child = rows,
            });
        }
    }

    private static Grid Line(string label, string value)
    {
        var g = new Grid { Padding = new Thickness(0, 3, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock { Text = label, FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        var v = new TextBlock { Text = value, FontSize = 12, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace") };
        Grid.SetColumn(v, 1);
        g.Children.Add(l);
        g.Children.Add(v);
        return g;
    }

    private static Border Chip(string text, Brush fg) => new()
    {
        Background = (Brush)Application.Current.Resources["LayerFillColorDefaultBrush"],
        CornerRadius = new CornerRadius(4),
        Padding = new Thickness(7, 1, 7, 2),
        VerticalAlignment = VerticalAlignment.Center,
        Child = new TextBlock { Text = text, FontSize = 11, Foreground = fg },
    };
}
