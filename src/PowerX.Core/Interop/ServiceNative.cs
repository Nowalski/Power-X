using System.Runtime.InteropServices;

namespace PowerX.Core.Interop;

/// <summary>
/// The Service Control Manager calls behind start-type changes. They exist because writing a
/// service's registry key directly does not reach the SCM's in-memory database until the next
/// boot (see <see cref="PowerX.Core.Services.ServiceProvider.SetStartMode"/>).
/// </summary>
internal static partial class ServiceNative
{
    internal const uint SC_MANAGER_CONNECT = 0x0001;
    internal const uint SERVICE_CHANGE_CONFIG = 0x0002;
    internal const uint SERVICE_NO_CHANGE = 0xFFFFFFFF;
    internal const uint SERVICE_CONFIG_DELAYED_AUTO_START_INFO = 3;

    internal const int ERROR_ACCESS_DENIED = 5;
    internal const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;

    [StructLayout(LayoutKind.Sequential)]
    internal struct SERVICE_DELAYED_AUTO_START_INFO
    {
        internal int fDelayedAutostart;   // BOOL
    }

    [LibraryImport("advapi32.dll", EntryPoint = "OpenSCManagerW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint OpenSCManagerW(string? machineName, string? databaseName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "OpenServiceW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial nint OpenServiceW(nint scManager, string serviceName, uint desiredAccess);

    [LibraryImport("advapi32.dll", EntryPoint = "ChangeServiceConfigW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ChangeServiceConfigW(
        nint service, uint serviceType, uint startType, uint errorControl,
        string? binaryPathName, string? loadOrderGroup, nint tagId, string? dependencies,
        string? serviceStartName, string? password, string? displayName);

    [LibraryImport("advapi32.dll", EntryPoint = "ChangeServiceConfig2W", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool ChangeServiceConfig2W(nint service, uint infoLevel, ref SERVICE_DELAYED_AUTO_START_INFO info);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseServiceHandle(nint handle);
}
