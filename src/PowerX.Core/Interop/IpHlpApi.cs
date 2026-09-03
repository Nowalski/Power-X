using System.Runtime.InteropServices;

namespace PowerX.Core.Interop;

/// <summary>TCP/UDP endpoint tables with owning PID — the data behind TCPView.</summary>
internal static partial class IpHlpApi
{
    internal const int AF_INET = 2;
    internal const int AF_INET6 = 23;

    // TCP_TABLE_CLASS
    internal const int TCP_TABLE_OWNER_PID_ALL = 5;
    // UDP_TABLE_CLASS
    internal const int UDP_TABLE_OWNER_PID = 1;

    [LibraryImport("iphlpapi.dll")]
    internal static partial uint GetExtendedTcpTable(nint table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order,
        int af, int tableClass, uint reserved);

    [LibraryImport("iphlpapi.dll")]
    internal static partial uint GetExtendedUdpTable(nint table, ref uint size, [MarshalAs(UnmanagedType.Bool)] bool order,
        int af, int tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MIB_TCPROW_OWNER_PID
    {
        internal uint State;
        internal uint LocalAddr;
        internal uint LocalPort;   // network byte order, low 16 bits
        internal uint RemoteAddr;
        internal uint RemotePort;
        internal uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MIB_UDPROW_OWNER_PID
    {
        internal uint LocalAddr;
        internal uint LocalPort;
        internal uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MIB_UDP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] internal byte[] LocalAddr;
        internal uint LocalScopeId;
        internal uint LocalPort;
        internal uint OwningPid;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MIB_TCP6ROW_OWNER_PID
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] internal byte[] LocalAddr;
        internal uint LocalScopeId;
        internal uint LocalPort;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] internal byte[] RemoteAddr;
        internal uint RemoteScopeId;
        internal uint RemotePort;
        internal uint State;
        internal uint OwningPid;
    }
}
