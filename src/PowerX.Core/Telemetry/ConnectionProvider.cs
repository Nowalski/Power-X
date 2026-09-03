using System.Net;
using System.Runtime.InteropServices;
using PowerX.Core.Interop;

namespace PowerX.Core.Telemetry;

public sealed record NetworkConnection
{
    public required string Protocol { get; init; }     // TCP / TCPv6 / UDP / UDPv6
    public required int Pid { get; init; }
    public required string ProcessName { get; init; }
    public required string LocalEndpoint { get; init; }
    public required string RemoteEndpoint { get; init; }
    public required string State { get; init; }
}

/// <summary>Active TCP/UDP endpoints with the owning process — TCPView-style, documented APIs.</summary>
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
            .ToList();
    }

    /// <summary>
    /// Probe for the table size, allocate, and read — retrying if the table grew between the two
    /// calls (a new socket appearing would otherwise make the whole read fail and return nothing).
    /// </summary>
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
                if (status != ErrorInsufficientBuffer) return;   // real failure — give up quietly
                // else: the table grew; `size` was updated, loop and retry with the bigger buffer
            }
            finally
            {
                Marshal.FreeHGlobal(buf);
            }
        }
    }

    private static string Name(IReadOnlyDictionary<int, string> names, int pid) =>
        names.GetValueOrDefault(pid, pid == 0 ? "System" : $"pid {pid}");

    private static unsafe void ReadTcp4(List<NetworkConnection> result, IReadOnlyDictionary<int, string> names) =>
        ReadTable(udp: false, IpHlpApi.AF_INET, buf =>
        {
            int count = *(int*)buf;
            var row = (IpHlpApi.MIB_TCPROW_OWNER_PID*)(buf + 4);
            for (int i = 0; i < count; i++, row++)
            {
                int pid = (int)row->OwningPid;
                result.Add(new NetworkConnection
                {
                    Protocol = "TCP",
                    Pid = pid,
                    ProcessName = Name(names, pid),
                    LocalEndpoint = $"{V4(row->LocalAddr)}:{Port(row->LocalPort)}",
                    RemoteEndpoint = row->State == 2 ? "*" : $"{V4(row->RemoteAddr)}:{Port(row->RemotePort)}",
                    State = row->State < TcpStates.Length ? TcpStates[row->State] : row->State.ToString(),
                });
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
                result.Add(new NetworkConnection
                {
                    Protocol = "UDP",
                    Pid = pid,
                    ProcessName = Name(names, pid),
                    LocalEndpoint = $"{V4(row->LocalAddr)}:{Port(row->LocalPort)}",
                    RemoteEndpoint = "*",
                    State = "",
                });
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
                result.Add(new NetworkConnection
                {
                    Protocol = "TCPv6",
                    Pid = pid,
                    ProcessName = Name(names, pid),
                    LocalEndpoint = $"[{new IPAddress(r.LocalAddr)}]:{Port(r.LocalPort)}",
                    RemoteEndpoint = r.State == 2 ? "*" : $"[{new IPAddress(r.RemoteAddr)}]:{Port(r.RemotePort)}",
                    State = r.State < TcpStates.Length ? TcpStates[r.State] : r.State.ToString(),
                });
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
                result.Add(new NetworkConnection
                {
                    Protocol = "UDPv6",
                    Pid = pid,
                    ProcessName = Name(names, pid),
                    LocalEndpoint = $"[{new IPAddress(r.LocalAddr)}]:{Port(r.LocalPort)}",
                    RemoteEndpoint = "*",
                    State = "",
                });
            }
        });

    private static string V4(uint addr) => new IPAddress(addr).ToString();

    private static int Port(uint netPort) => ((int)(netPort & 0xFF) << 8) | (int)((netPort >> 8) & 0xFF);
}
