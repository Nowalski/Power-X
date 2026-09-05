using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace PowerX.Core.Telemetry;

/// <summary>
/// Per-interface throughput and addressing via <c>System.Net.NetworkInformation</c>
/// (documented, managed). Rates are deltas against the previous sample — keep one instance.
/// </summary>
public sealed class NetworkMetricsProvider
{
    private readonly ILogger _log;
    private Dictionary<string, (long Rx, long Tx, DateTimeOffset At)> _prev = new();

    // GetAllNetworkInterfaces() rebuilds an object for every adapter on the machine, which on a
    // box with a lot of virtual and filter adapters is most of a sampling tick all by itself
    // (measured: 10-12 ms for 55 adapters, against 0.7 ms to read the counters afterwards).
    // The byte counters on an already-enumerated interface stay live - GetIPStatistics() queries
    // the adapter each time it is called - so the enumeration is kept and only refreshed
    // periodically, while the counters behind the rates are still read every single tick.
    // The trade is that a change in the adapter *set* or in link state can be up to this old.
    private static readonly TimeSpan TopologyMaxAge = TimeSpan.FromSeconds(5);
    private NetworkInterface[] _nics = [];
    private DateTimeOffset _nicsAt = DateTimeOffset.MinValue;

    public NetworkMetricsProvider(ILogger<NetworkMetricsProvider>? log = null)
        => _log = log ?? NullLogger<NetworkMetricsProvider>.Instance;

    private NetworkInterface[] Interfaces(DateTimeOffset now)
    {
        if (_nics.Length > 0 && now - _nicsAt < TopologyMaxAge) return _nics;
        _nics = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel))
            .Where(n => !IsPseudoInterface(n))
            .ToArray();
        _nicsAt = now;
        return _nics;
    }

    public ProviderResult<NetworkMetrics> Sample()
    {
        try
        {
            var now = DateTimeOffset.UtcNow;
            var list = new List<NetworkInterfaceMetrics>();
            var next = new Dictionary<string, (long, long, DateTimeOffset)>();

            foreach (var nic in Interfaces(now))
            {
                IPInterfaceStatistics stats;
                try { stats = nic.GetIPStatistics(); }
                catch (NetworkInformationException) { continue; }

                long rx = stats.BytesReceived, tx = stats.BytesSent;
                double rxRate = 0, txRate = 0;
                if (_prev.TryGetValue(nic.Id, out var p))
                {
                    double secs = (now - p.At).TotalSeconds;
                    if (secs > 0)
                    {
                        rxRate = Math.Max(0, (rx - p.Rx) / secs);
                        txRate = Math.Max(0, (tx - p.Tx) / secs);
                    }
                }
                next[nic.Id] = (rx, tx, now);

                IPInterfaceProperties props;
                try { props = nic.GetIPProperties(); }
                catch (NetworkInformationException) { props = null!; }

                list.Add(new NetworkInterfaceMetrics
                {
                    Name = nic.Name,
                    Description = nic.Description,
                    Type = FriendlyType(nic.NetworkInterfaceType),
                    IsUp = nic.OperationalStatus == OperationalStatus.Up,
                    LinkSpeedBps = nic.Speed > 0 ? nic.Speed : 0,
                    SendBytesPerSec = txRate,
                    ReceiveBytesPerSec = rxRate,
                    TotalBytesSent = tx,
                    TotalBytesReceived = rx,
                    MacAddress = FormatMac(nic.GetPhysicalAddress().GetAddressBytes()),
                    IpAddresses = props?.UnicastAddresses
                        .Where(a => a.Address.AddressFamily is System.Net.Sockets.AddressFamily.InterNetwork or System.Net.Sockets.AddressFamily.InterNetworkV6)
                        .Select(a => a.Address.ToString()).ToList() ?? [],
                    Gateways = props?.GatewayAddresses.Select(g => g.Address.ToString()).ToList() ?? [],
                    DnsServers = props?.DnsAddresses.Select(d => d.ToString()).ToList() ?? [],
                });
            }

            _prev = next;
            return ProviderResult<NetworkMetrics>.Ok(new NetworkMetrics(
                list.OrderByDescending(i => i.IsUp).ThenByDescending(i => i.ReceiveBytesPerSec + i.SendBytesPerSec).ToList(),
                now));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Network sample failed");
            return ProviderResult<NetworkMetrics>.NotAvailable(ex.Message);
        }
    }

    private static readonly string[] PseudoMarkers =
    [
        "WFP", "QoS Packet Scheduler", "Native MAC Layer", "LightWeight Filter",
        "Filter Driver", "Miniport", "Kernel Debug", "Pseudo-Interface",
        "Teredo", "ISATAP", "6to4", "Virtual WiFi Filter",
    ];

    private static bool IsPseudoInterface(NetworkInterface nic)
    {
        string d = nic.Description;
        if (PseudoMarkers.Any(m => d.Contains(m, StringComparison.OrdinalIgnoreCase))) return true;
        // real Ethernet/Wi-Fi adapters have a MAC address
        if (nic.NetworkInterfaceType is NetworkInterfaceType.Ethernet
            or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.Wireless80211
            && nic.GetPhysicalAddress().GetAddressBytes().Length == 0) return true;
        return false;
    }

    private static string FriendlyType(NetworkInterfaceType t) => t switch
    {
        NetworkInterfaceType.Ethernet or NetworkInterfaceType.GigabitEthernet or NetworkInterfaceType.FastEthernetT => "Ethernet",
        NetworkInterfaceType.Wireless80211 => "Wi-Fi",
        NetworkInterfaceType.Ppp => "PPP",
        _ => t.ToString(),
    };

    private static string FormatMac(byte[] bytes) =>
        bytes.Length == 0 ? "" : string.Join(":", bytes.Select(b => b.ToString("X2")));
}
