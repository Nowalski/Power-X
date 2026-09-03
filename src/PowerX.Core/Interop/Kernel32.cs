using System.Runtime.InteropServices;

namespace PowerX.Core.Interop;

internal static partial class Kernel32
{
    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemTimes(out long idle, out long kernel, out long user);

    [StructLayout(LayoutKind.Sequential)]
    internal struct MEMORYSTATUSEX
    {
        internal uint dwLength;
        internal uint dwMemoryLoad;
        internal ulong ullTotalPhys;
        internal ulong ullAvailPhys;
        internal ulong ullTotalPageFile;
        internal ulong ullAvailPageFile;
        internal ulong ullTotalVirtual;
        internal ulong ullAvailVirtual;
        internal ulong ullAvailExtendedVirtual;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [StructLayout(LayoutKind.Sequential)]
    internal struct PERFORMANCE_INFORMATION
    {
        internal uint cb;
        internal nuint CommitTotal;
        internal nuint CommitLimit;
        internal nuint CommitPeak;
        internal nuint PhysicalTotal;
        internal nuint PhysicalAvailable;
        internal nuint SystemCache;
        internal nuint KernelTotal;
        internal nuint KernelPaged;
        internal nuint KernelNonpaged;
        internal nuint PageSize;
        internal uint HandleCount;
        internal uint ProcessCount;
        internal uint ThreadCount;
    }

    [LibraryImport("psapi.dll", EntryPoint = "GetPerformanceInfo", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetPerformanceInfo(ref PERFORMANCE_INFORMATION pi, uint cb);

    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool QueryFullProcessImageNameW(nint process, uint flags, Span<char> exeName, ref uint size);

    [LibraryImport("kernel32.dll")]
    internal static partial int GetActiveProcessorCount(ushort groupNumber);

    internal const ushort ALL_PROCESSOR_GROUPS = 0xffff;
}
