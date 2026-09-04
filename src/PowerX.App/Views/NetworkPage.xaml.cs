using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using PowerX.App.Services;
using PowerX.Core.Telemetry;

namespace PowerX.App.Views;

public sealed record ConnVm(string Name, string Proto, string Local, string Remote, string State);
public sealed record ListenVm(string Port, string Proto, string Process, string Bound, string Reach, Brush ReachBrush);

public sealed partial class NetworkPage : Page
{
    private IDisposable? _subscription;
    private int _tick;
    private string _connFilter = "";
    private IReadOnlyList<NetworkConnection> _connections = [];
    private bool _onPage;
    private bool _refreshingConnections;
    private bool _resolve;
    private CancellationTokenSource? _resolveCts;

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
        _diagCts?.Cancel();
        _resolveCts?.Cancel();
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

    // ---------------------------------------------------------------- live tick

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

    // ---------------------------------------------------------------- connections

    private async Task RefreshConnectionsAsync()
    {
        if (_refreshingConnections) return;
        _refreshingConnections = true;
        try
        {
            IReadOnlyList<NetworkConnection> conns;
            if (DemoData.Active)
            {
                conns = DemoData.Connections();
            }
            else
            {
                var names = TelemetryHub.Instance.LastProcesses?.Processes
                    .GroupBy(p => p.Pid).ToDictionary(g => g.Key, g => g.First().Name)
                    ?? new Dictionary<int, string>();
                conns = await Task.Run(() => ConnectionProvider.Enumerate(names));
            }
            if (!_onPage) return;
            _connections = conns;

            var s = ConnectionProvider.Summarize(conns);
            ConnSummary.Text = $"{s.Total} connections   {s.Established} established   {s.Listening} listening"
                             + (s.TimeWait > 0 ? $"   {s.TimeWait} time-wait" : "")
                             + (s.Udp > 0 ? $"   {s.Udp} UDP" : "");

            RenderListening();
            RenderConnections();
            if (_resolve) StartResolving();
        }
        finally
        {
            _refreshingConnections = false;
        }
    }

    private void RenderListening()
    {
        var ports = ConnectionProvider.ListeningPorts(_connections);
        int exposed = ports.Count(p => p.Exposed);
        ListenSummary.Text = ports.Count == 0
            ? "Nothing is listening for connections."
            : $"{ports.Count} listening   {exposed} reachable from the network";

        var caution = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"];
        var muted = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"];
        ListenList.ItemsSource = ports.Select(p => new ListenVm(
            p.LocalPort.ToString(),
            p.Protocol,
            p.ProcessName,
            p.LocalAddress switch { "0.0.0.0" => "any (IPv4)", "::" => "any (IPv6)", var a => a },
            p.Exposed ? "network" : "local only",
            p.Exposed ? caution : muted)).ToList();
    }

    private void ConnFilter_Changed(object sender, TextChangedEventArgs e)
    {
        _connFilter = ConnFilter.Text.Trim();
        RenderConnections();
    }

    private void Resolve_Toggled(object sender, RoutedEventArgs e)
    {
        _resolve = ResolveToggle.IsChecked == true;
        _resolveCts?.Cancel();
        if (_resolve) StartResolving();
        RenderConnections();
    }

    private void StartResolving()
    {
        var targets = _connections
            .Where(c => c.RemoteAddress is { } ip && ReverseDns.IsResolvable(ip) && !ReverseDns.Attempted(ip))
            .Select(c => c.RemoteAddress!)
            .Distinct()
            .Take(60)
            .ToList();
        if (targets.Count == 0) return;

        // A superseded resolve loop just stops; the CTS carries no timer so GC handles it.
        _resolveCts?.Cancel();
        var cts = new CancellationTokenSource();
        _resolveCts = cts;

        _ = Task.Run(async () =>
        {
            foreach (var ip in targets)
            {
                if (cts.IsCancellationRequested) return;
                await ReverseDns.ResolveAsync(ip, cts.Token);
            }
            if (!cts.IsCancellationRequested)
                DispatcherQueue.TryEnqueue(() => { if (_onPage && _resolve) RenderConnections(); });
        }, cts.Token);
    }

    private IReadOnlyList<NetworkConnection> FilteredConnections()
    {
        IEnumerable<NetworkConnection> src = _connections;
        if (_connFilter.Length > 0)
            src = src.Where(c =>
                c.ProcessName.Contains(_connFilter, StringComparison.OrdinalIgnoreCase) ||
                c.RemoteEndpoint.Contains(_connFilter, StringComparison.OrdinalIgnoreCase) ||
                (c.RemoteAddress is { } ip && (ReverseDns.Cached(ip)?.Contains(_connFilter, StringComparison.OrdinalIgnoreCase) ?? false)));
        return src.ToList();
    }

    private string RemoteText(NetworkConnection c)
    {
        if (c.RemoteAddress is null) return "*";
        if (_resolve && ReverseDns.Cached(c.RemoteAddress) is { } name)
            return c.RemotePort > 0 ? $"{name}:{c.RemotePort}" : name;
        return c.RemoteEndpoint;
    }

    private void RenderConnections() =>
        Connections.ItemsSource = FilteredConnections().Take(400)
            .Select(c => new ConnVm(c.ProcessName, c.Protocol, c.LocalEndpoint, RemoteText(c), c.State))
            .ToList();

    private void CopyConn_Click(object sender, RoutedEventArgs e)
    {
        var rows = FilteredConnections();
        var sb = new System.Text.StringBuilder("Process\tProtocol\tLocal\tRemote\tState\n");
        foreach (var c in rows)
            sb.Append(c.ProcessName).Append('\t').Append(c.Protocol).Append('\t')
              .Append(c.LocalEndpoint).Append('\t').Append(RemoteText(c)).Append('\t').Append(c.State).Append('\n');
        CopyConnButton.Content = Services.Clip.SetText(sb.ToString()) ? "Copied" : "Copy failed";
        _ = ResetCopyLabel();
    }

    private async Task ResetCopyLabel()
    {
        await Task.Delay(1400);
        if (_onPage) CopyConnButton.Content = "Copy";
    }

    // ---------------------------------------------------------------- interfaces

    private string _interfaceSig = "";
    private readonly List<TextBlock> _ifaceThroughput = [];

    private void RebuildInterfaces(NetworkMetrics net)
    {
        // The card contents (name, IP, state, link speed) rarely change tick to tick; only the
        // throughput numbers do. Skip the full teardown-and-rebuild unless the set of interfaces
        // or their addresses actually changed — just refresh the throughput line in place.
        string sig = string.Join("|", net.Interfaces.Select(i =>
            $"{i.Name};{i.IsUp};{i.LinkSpeedBps};{string.Join(",", i.IpAddresses)}"));
        if (sig == _interfaceSig && _ifaceThroughput.Count == net.Interfaces.Count)
        {
            for (int n = 0; n < net.Interfaces.Count; n++)
                _ifaceThroughput[n].Text = $"down {Fmt.Rate(net.Interfaces[n].ReceiveBytesPerSec)}    up {Fmt.Rate(net.Interfaces[n].SendBytesPerSec)}";
            return;
        }
        _interfaceSig = sig;
        _ifaceThroughput.Clear();

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

            var (throughputRow, throughputValue) = LinePair("Throughput",
                $"down {Fmt.Rate(i.ReceiveBytesPerSec)}    up {Fmt.Rate(i.SendBytesPerSec)}");
            _ifaceThroughput.Add(throughputValue);
            rows.Children.Add(throughputRow);
            if (i.LinkSpeedBps > 0)
                rows.Children.Add(Line("Link speed", $"{i.LinkSpeedBps / 1_000_000:N0} Mbps"));
            rows.Children.Add(Line("Total", $"received {Fmt.Bytes((ulong)i.TotalBytesReceived)}, sent {Fmt.Bytes((ulong)i.TotalBytesSent)}"));
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

    private static Grid Line(string label, string value) => LinePair(label, value).Row;

    private static (Grid Row, TextBlock Value) LinePair(string label, string value)
    {
        var g = new Grid { Padding = new Thickness(0, 3, 0, 0) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var l = new TextBlock { Text = label, FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] };
        var v = new TextBlock { Text = value, FontSize = 12, TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas, Cascadia Mono, monospace") };
        Grid.SetColumn(v, 1);
        g.Children.Add(l);
        g.Children.Add(v);
        return (g, v);
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
