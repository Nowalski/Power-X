using System.Runtime.InteropServices;

namespace PowerX.Core.Interop;

/// <summary>
/// Minimal, documented-where-possible NT API surface used for system-wide telemetry.
/// We prefer a single <c>NtQuerySystemInformation</c> call over thousands of per-process
/// Win32 calls: it is how Task Manager / Process Explorer class tools stay cheap.
/// </summary>
internal static partial class NtDll
{
    internal const int SystemProcessorPerformanceInformation = 8;
    internal const int SystemProcessInformation = 5;

    [LibraryImport("ntdll.dll")]
    internal static partial int NtQuerySystemInformation(
        int systemInformationClass,
        nint systemInformation,
        uint systemInformationLength,
        out uint returnLength);

    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
    {
        internal long IdleTime;
        internal long KernelTime; // includes idle
        internal long UserTime;
        internal long DpcTime;
        internal long InterruptTime;
        internal uint InterruptCount;
    }

    // SYSTEM_PROCESS_INFORMATION — layout for x64. Offsets are stable and documented across
    // the community (winternl.h ships a partial version). We only read fields we rely on.
    [StructLayout(LayoutKind.Sequential)]
    internal struct SYSTEM_PROCESS_INFORMATION
    {
        internal uint NextEntryOffset;
        internal uint NumberOfThreads;
        internal long WorkingSetPrivateSize;
        internal uint HardFaultCount;
        internal uint NumberOfThreadsHighWatermark;
        internal ulong CycleTime;
        internal long CreateTime;
        internal long UserTime;
        internal long KernelTime;
        internal ushort ImageNameLength;      // bytes
        internal ushort ImageNameMaximumLength;
        internal nint ImageNameBuffer;        // PWSTR
        internal int BasePriority;
        internal nint UniqueProcessId;
        internal nint InheritedFromUniqueProcessId;
        internal uint HandleCount;
        internal uint SessionId;
        internal nint UniqueProcessKey;
        internal nuint PeakVirtualSize;
        internal nuint VirtualSize;
        internal uint PageFaultCount;
        internal nuint PeakWorkingSetSize;
        internal nuint WorkingSetSize;
        internal nuint QuotaPeakPagedPoolUsage;
        internal nuint QuotaPagedPoolUsage;
        internal nuint QuotaPeakNonPagedPoolUsage;
        internal nuint QuotaNonPagedPoolUsage;
        internal nuint PagefileUsage;
        internal nuint PeakPagefileUsage;
        internal nuint PrivatePageCount;
        internal long ReadOperationCount;
        internal long WriteOperationCount;
        internal long OtherOperationCount;
        internal long ReadTransferCount;
        internal long WriteTransferCount;
        internal long OtherTransferCount;
    }
}
