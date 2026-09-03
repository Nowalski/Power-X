using System.Runtime.InteropServices;

namespace PowerX.Core.Interop;

internal static partial class ProcessNative
{
    internal const uint PROCESS_TERMINATE = 0x0001;
    internal const uint PROCESS_SET_INFORMATION = 0x0200;
    internal const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    internal const uint PROCESS_SUSPEND_RESUME = 0x0800;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial nint OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint pid);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(nint handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool TerminateProcess(nint handle, uint exitCode);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetPriorityClass(nint handle, uint priorityClass);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial uint GetPriorityClass(nint handle);

    [LibraryImport("ntdll.dll")]
    internal static partial int NtSuspendProcess(nint handle);

    [LibraryImport("ntdll.dll")]
    internal static partial int NtResumeProcess(nint handle);

    // ---- EcoQoS / efficiency mode ----
    internal const int ProcessPowerThrottling = 4;
    internal const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
    internal const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct PROCESS_POWER_THROTTLING_STATE
    {
        internal uint Version;
        internal uint ControlMask;
        internal uint StateMask;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetProcessInformation(nint handle, int informationClass, ref PROCESS_POWER_THROTTLING_STATE info, uint size);

    // ---- token: elevation + integrity ----
    internal const uint TOKEN_QUERY = 0x0008;
    internal const int TokenElevation = 20;
    internal const int TokenIntegrityLevel = 25;

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenProcessToken(nint process, uint access, out nint token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetTokenInformation(nint token, int infoClass, nint info, uint infoLength, out uint returnLength);

    internal const uint PROCESS_QUERY_INFORMATION = 0x0400;
    internal const uint PROCESS_VM_READ = 0x0010;
    internal const uint LIST_MODULES_ALL = 0x03;

    [LibraryImport("psapi.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool EnumProcessModulesEx(nint process, [Out] nint[] modules, uint cb, out uint needed, uint filterFlag);

    [LibraryImport("psapi.dll", SetLastError = true, EntryPoint = "GetModuleFileNameExW", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint GetModuleFileNameExW(nint process, nint module, Span<char> baseName, uint size);
}
