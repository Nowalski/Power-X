using System.Net;
using System.Runtime.InteropServices;
using PowerX.Core.Interop;

namespace PowerX.Core.Telemetry;

public sealed record NetworkConnection
{
    public required string Protocol { get; init; }     // TCP / TCPv6 / UDP / UDPv6
    public required int Pid { get; init; }
    public required string ProcessName { get; init; }

    public required string LocalAddress { get; init; }
    public required int LocalPort { get; init; }

    /// <summary>Null for a listening socket or an unbound UDP endpoint.</summary>
    public required string? RemoteAddress { get; init; }
    public required int RemotePort { get; init; }

    public required string State { get; init; }         // "" for UDP
    public required bool IsListening { get; init; }     // TCP LISTEN, or a UDP endpoint with no peer
    /// <summary>A listening socket bound to something other than loopback, so it is reachable from the network.</summary>
    public required bool Exposed { get; init; }

    public bool IsV6 => Protocol.EndsWith("v6", StringComparison.Ordinal);
    public bool IsTcp => Protocol.StartsWith("TCP", StringComparison.Ordinal);

    public string LocalEndpoint => Endpoint(LocalAddress, LocalPort);
    public string RemoteEndpoint => RemoteAddress is null ? "*" : Endpoint(RemoteAddress, RemotePort);

    private static string Endpoint(string addr, int port) =>
        addr.Contains(':') ? $"[{addr}]:{port}" : $"{addr}:{port}";
}

public sealed record ConnectionSummary(int Total, int Established, int Listening, int TimeWait, int OtherTcp, int Udp);

/// <summary>Active TCP/UDP endpoints with the owning process, TCPView style, documented APIs.</summary>
public static class ConnectionProvider
{
    private const uint ErrorInsufficientBuffer = 122;

    private static readonly string[] TcpStates =
    [
        "", "CLOSED", "LISTEN", "SYN-SENT", "SYN-RCVD", "ESTABLISHED", "FIN-WAIT-1",
        "FIN-WAIT-2", "CLOSE-WAIT", "CLOSING", "LAST-ACK", "TIME-WAIT", "DELETE-TCB",
    ];

    public static IReadOnlyList<NetworkConnection> Enumerate(IReadOnlyDictionary<int, string> pidNames)
    {
        var result = new List<NetworkConnection>(256);
        ReadTcp4(result, pidNames);
        ReadTcp6(result, pidNames);
        ReadUdp4(result, pidNames);
        ReadUdp6(result, pidNames);
        return result
            .OrderBy(c => c.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Protocol, StringComparer.Ordinal)
            .ThenBy(c => c.LocalPort)
            .ToList();
    }

    public static ConnectionSummary Summarize(IReadOnlyList<NetworkConnection> conns) => new(
        conns.Count,
        conns.Count(c => c.State == "ESTABLISHED"),
        conns.Count(c => c.IsListening),
        conns.Count(c => c.State == "TIME-WAIT"),
        conns.Count(c => c.IsTcp && c.State is not ("ESTABLISHED" or "TIME-WAIT" or "LISTEN" or "")),
        conns.Count(c => !c.IsTcp));

    /// <summary>The listening sockets only, one row per bound port, sorted by port.</summary>
    public static IReadOnlyList<NetworkConnection> ListeningPorts(IReadOnlyList<NetworkConnection> conns) => conns
        .Where(c => c.IsListening)
        .GroupBy(c => (c.Protocol, c.LocalPort, c.Pid))
        .Select(g => g.First())
        .OrderBy(c => c.LocalPort)
        .ThenBy(c => c.Protocol, StringComparer.Ordinal)
        .ToList();

    private static void ReadTable(bool udp, int af, Action<nint> parse)
    {
        uint size = 0;
        for (int attempt = 0; attempt < 4; attempt++)
        {
            uint status = udp
                ? IpHlpApi.GetExtendedUdpTable(0, ref size, true, af, IpHlpApi.UDP_TABLE_OWNER_PID, 0)
                : IpHlpApi.GetExtendedTcpTable(0, ref size, true, af, IpHlpApi.TCP_TABLE_OWNER_PID_ALL, 0);
            if (size == 0) return;

            nint buf = Marshal.AllocHGlobal((int)size);
            try
            {
                status = udp
                    ? IpHlpApi.GetExtendedUdpTable(buf, ref size, true, af, IpHlpApi.UDP_TABLE_OWNER_PID, 0)
                    : IpHlpApi.GetExtendedTcpTable(buf, ref size, true, af, IpHlpApi.TCP_TABLE_OWNER_PID_ALL, 0);

                if (status == 0) { parse(buf); return; }
                if (status != ErrorInsufficientBuffer) return;
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
    }

    private static string Name(IReadOnlyDictionary<int, string> names, int pid) =>
        names.GetValueOrDefault(pid, pid == 0 ? "System" : $"pid {pid}");

    private static bool IsLoopback(string ip) => ip.StartsWith("127.", StringComparison.Ordinal) || ip == "::1";

    private static NetworkConnection Tcp(string proto, int pid, string name, string localAddr, int localPort,
        string remoteAddr, int remotePort, uint stateCode)
    {
        string state = stateCode < TcpStates.Length ? TcpStates[stateCode] : stateCode.ToString();
        bool listening = state == "LISTEN";
        return new NetworkConnection
        {
            Protocol = proto, Pid = pid, ProcessName = name,
            LocalAddress = localAddr, LocalPort = localPort,
            RemoteAddress = listening ? null : remoteAddr, RemotePort = listening ? 0 : remotePort,
            State = state,
            IsListening = listening,
            Exposed = listening && !IsLoopback(localAddr),
        };
    }

    private static NetworkConnection Udp(string proto, int pid, string name, string localAddr, int localPort) =>
        new()
        {
            Protocol = proto, Pid = pid, ProcessName = name,
            LocalAddress = localAddr, LocalPort = localPort,
            RemoteAddress = null, RemotePort = 0,
            State = "",
            IsListening = true,   // an owned UDP endpoint is bound and can receive
            Exposed = !IsLoopback(localAddr),
        };

    private static unsafe void ReadTcp4(List<NetworkConnection> result, IReadOnlyDictionary<int, string> names) =>
        ReadTable(udp: false, IpHlpApi.AF_INET, buf =>
        {
            int count = *(int*)buf;
            var row = (IpHlpApi.MIB_TCPROW_OWNER_PID*)(buf + 4);
            for (int i = 0; i < count; i++, row++)
            {
                int pid = (int)row->OwningPid;
                result.Add(Tcp("TCP", pid, Name(names, pid),
                    V4(row->LocalAddr), Port(row->LocalPort),
                    V4(row->RemoteAddr), Port(row->RemotePort), row->State));
            }
        });

    private static unsafe void ReadUdp4(List<NetworkConnection> result, IReadOnlyDictionary<int, string> names) =>
        ReadTable(udp: true, IpHlpApi.AF_INET, buf =>
        {
            int count = *(int*)buf;
            var row = (IpHlpApi.MIB_UDPROW_OWNER_PID*)(buf + 4);
            for (int i = 0; i < count; i++, row++)
            {
                int pid = (int)row->OwningPid;
                result.Add(Udp("UDP", pid, Name(names, pid), V4(row->LocalAddr), Port(row->LocalPort)));
            }
        });

    private static void ReadTcp6(List<NetworkConnection> result, IReadOnlyDictionary<int, string> names) =>
        ReadTable(udp: false, IpHlpApi.AF_INET6, buf =>
        {
            int count = Marshal.ReadInt32(buf);
            int stride = Marshal.SizeOf<IpHlpApi.MIB_TCP6ROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var r = Marshal.PtrToStructure<IpHlpApi.MIB_TCP6ROW_OWNER_PID>(buf + 4 + i * stride);
                int pid = (int)r.OwningPid;
                result.Add(Tcp("TCPv6", pid, Name(names, pid),
                    new IPAddress(r.LocalAddr).ToString(), Port(r.LocalPort),
                    new IPAddress(r.RemoteAddr).ToString(), Port(r.RemotePort), r.State));
            }
        });

    private static void ReadUdp6(List<NetworkConnection> result, IReadOnlyDictionary<int, string> names) =>
        ReadTable(udp: true, IpHlpApi.AF_INET6, buf =>
        {
            int count = Marshal.ReadInt32(buf);
            int stride = Marshal.SizeOf<IpHlpApi.MIB_UDP6ROW_OWNER_PID>();
            for (int i = 0; i < count; i++)
            {
                var r = Marshal.PtrToStructure<IpHlpApi.MIB_UDP6ROW_OWNER_PID>(buf + 4 + i * stride);
                int pid = (int)r.OwningPid;
                result.Add(Udp("UDPv6", pid, Name(names, pid), new IPAddress(r.LocalAddr).ToString(), Port(r.LocalPort)));
            }
        });

    private static string V4(uint addr) => new IPAddress(addr).ToString();

    private static int Port(uint netPort) => ((int)(netPort & 0xFF) << 8) | (int)((netPort >> 8) & 0xFF);
}
