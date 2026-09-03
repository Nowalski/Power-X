using System.Runtime.InteropServices;

namespace PowerX.Core.Interop;

internal static partial class SystemInfoNative
{
    // ---- GetLogicalProcessorInformationEx ----
    internal enum LOGICAL_PROCESSOR_RELATIONSHIP
    {
        RelationProcessorCore = 0,
        RelationNumaNode = 1,
        RelationCache = 2,
        RelationProcessorPackage = 3,
        RelationGroup = 4,
        RelationAll = 0xffff,
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetLogicalProcessorInformationEx(
        LOGICAL_PROCESSOR_RELATIONSHIP relationship,
        nint buffer,
        ref uint returnedLength);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetPhysicallyInstalledSystemMemory(out ulong totalMemoryInKilobytes);

    // PF_ values for IsProcessorFeaturePresent
    internal const uint PF_VIRT_FIRMWARE_ENABLED = 21;
    internal const uint PF_SECOND_LEVEL_ADDRESS_TRANSLATION = 20;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool IsProcessorFeaturePresent(uint processorFeature);

    // ---- CallNtPowerInformation(ProcessorInformation) ----
    internal const int ProcessorInformation = 11;

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESSOR_POWER_INFORMATION
    {
        internal uint Number;
        internal uint MaxMhz;
        internal uint CurrentMhz;
        internal uint MhzLimit;
        internal uint MaxIdleState;
        internal uint CurrentIdleState;
    }

    [LibraryImport("powrprof.dll")]
    internal static partial uint CallNtPowerInformation(
        int informationLevel,
        nint inputBuffer,
        uint inputBufferLength,
        nint outputBuffer,
        uint outputBufferLength);
}
