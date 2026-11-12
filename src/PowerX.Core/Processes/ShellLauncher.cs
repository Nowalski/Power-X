using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace PowerX.Core.Processes;

/// <summary>
/// Starts programs with the signed-in user's normal rights instead of PowerX's.
///
/// PowerX runs elevated, and anything it starts the usual way inherits that: a web link opens
/// an elevated browser, and an uninstaller from a per-user (HKCU) registry entry, which any
/// program the user runs can write, would run as administrator without a UAC prompt. Settings
/// runs those unelevated, so PowerX should too.
///
/// It works by naming a non-elevated process of this user in this session (normally the
/// desktop shell) as the new process's parent: Windows gives a child its parent's token. A
/// program whose manifest demands elevation then shows the normal UAC prompt, exactly as it
/// would when started from Explorer.
/// </summary>
public static partial class ShellLauncher
{
    /// <summary>Processes that normally run unelevated in every interactive session, best first.</summary>
    private static readonly string[] ParentCandidates =
        ["explorer", "sihost", "ctfmon", "taskhostw", "RuntimeBroker"];

    /// <summary>
    /// Open a web address or document the way double-clicking it in Explorer would, unelevated.
    /// Falls back to an ordinary start when there is no unelevated process to borrow, so a link
    /// still opens.
    /// </summary>
    public static ActionResult Open(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return ActionResult.Fail("Nothing to open.");
        string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        if (TryStart(explorer, Quote(target), out _) is null) return ActionResult.Ok;
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return ActionResult.Ok;
        }
        catch (Exception ex)
        {
            return ActionResult.Fail(ex.Message);
        }
    }

    /// <summary>
    /// Start <paramref name="file"/> with <paramref name="arguments"/> unelevated. Never falls
    /// back to starting it elevated: the caller asked for this because the command is not
    /// trusted with administrator rights.
    /// </summary>
    public static ActionResult StartUnelevated(string file, string arguments)
    {
        string? error = TryStart(file, arguments, out _);
        return error is null ? ActionResult.Ok : ActionResult.Fail(error);
    }

    /// <summary>Returns null on success, otherwise why it could not start.</summary>
    internal static string? TryStart(string file, string arguments, out int pid)
    {
        pid = 0;
        string commandLine = arguments.Length == 0 ? Quote(file) : Quote(file) + " " + arguments;

        int session;
        using (var self = Process.GetCurrentProcess()) session = self.SessionId;
        SecurityIdentifier? me = WindowsIdentity.GetCurrent().User;

        string? lastError = null;
        foreach (var name in ParentCandidates)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                using (p)
                {
                    int parentId;
                    try
                    {
                        if (p.SessionId != session) continue;
                        parentId = p.Id;
                    }
                    catch (Exception) { continue; }

                    using var parent = OpenProcess(PROCESS_CREATE_PROCESS | PROCESS_QUERY_LIMITED_INFORMATION, false, parentId);
                    if (parent.IsInvalid) continue;
                    if (!IsUnelevatedProcessOf(parent, me)) continue;

                    int err = CreateWithParent(parent, file, commandLine, out pid);
                    if (err == 0) return null;
                    if (err == ERROR_ELEVATION_REQUIRED)
                    {
                        // CreateProcess cannot show a UAC prompt; ShellExecute can. Hand the
                        // command to an unelevated PowerShell, which starts it through
                        // ShellExecute so its own manifest gets the usual UAC prompt.
                        err = CreateWithParent(parent, PowerShellPath, PowerShellStartArgs(file, arguments), out pid);
                        if (err == 0) return null;
                    }
                    lastError = new Win32Exception(err).Message;
                }
            }
        }
        return lastError ?? "There is no unelevated program of yours running in this session to start it from.";
    }

    internal static string Quote(string s) =>
        s.Length > 1 && s[0] == '"' && s[^1] == '"' ? s : "\"" + s.Replace("\"", "") + "\"";

    private static string PowerShellPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    /// <summary>
    /// The arguments for an unelevated PowerShell that runs <c>Start-Process</c> on the command.
    /// The script travels base64-encoded, so no character in the command can break out of it.
    /// </summary>
    internal static string PowerShellStartArgs(string file, string arguments)
    {
        static string Lit(string s) => "'" + s.Replace("'", "''") + "'";
        string script = arguments.Length == 0
            ? $"Start-Process -FilePath {Lit(file)}"
            : $"Start-Process -FilePath {Lit(file)} -ArgumentList {Lit(arguments)}";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return $"-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand {encoded}";
    }

    private static bool IsUnelevatedProcessOf(SafeProcessHandle process, SecurityIdentifier? user)
    {
        if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) return false;
        using (token)
        {
            if (!GetTokenInformation(token, TokenElevation, out int elevated, sizeof(int), out _) || elevated != 0)
                return false;
            if (user is null) return true;
            try
            {
                using var identity = new WindowsIdentity(token.DangerousGetHandle());
                return identity.User is { } owner && user.Equals(owner);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    private static unsafe int CreateWithParent(SafeProcessHandle parent, string file, string commandLine, out int pid)
    {
        pid = 0;
        nint size = 0;
        InitializeProcThreadAttributeList(0, 1, 0, ref size);
        nint list = Marshal.AllocHGlobal(size);
        bool listReady = false;
        bool addedRef = false;
        try
        {
            if (!InitializeProcThreadAttributeList(list, 1, 0, ref size)) return Marshal.GetLastPInvokeError();
            listReady = true;

            parent.DangerousAddRef(ref addedRef);
            nint parentHandle = parent.DangerousGetHandle();
            if (!UpdateProcThreadAttribute(list, 0, PROC_THREAD_ATTRIBUTE_PARENT_PROCESS,
                    &parentHandle, (nint)sizeof(nint), 0, 0))
                return Marshal.GetLastPInvokeError();

            var si = new STARTUPINFOEXW { lpAttributeList = list };
            si.cb = sizeof(STARTUPINFOEXW);

            string? dir = Path.IsPathRooted(file) ? Path.GetDirectoryName(file) : null;
            if (dir is not null && !Directory.Exists(dir)) dir = null;

            char[] cmd = (commandLine + "\0").ToCharArray();   // CreateProcessW may write to it
            fixed (char* cmdPtr = cmd)
            {
                // Only a full path goes in as the application name: that argument is not searched
                // for on PATH, so "MsiExec.exe /X{...}" must be found through the command line.
                string? application = Path.IsPathRooted(file) ? file : null;
                if (!CreateProcessW(application, cmdPtr, 0, 0, false, EXTENDED_STARTUPINFO_PRESENT, 0, dir,
                        ref si, out var pi))
                    return Marshal.GetLastPInvokeError();
                pid = pi.dwProcessId;
                CloseHandle(pi.hThread);
                CloseHandle(pi.hProcess);
            }
            return 0;
        }
        finally
        {
            if (addedRef) parent.DangerousRelease();
            if (listReady) DeleteProcThreadAttributeList(list);
            Marshal.FreeHGlobal(list);
        }
    }

    // ---------------------------------------------------------------- interop

    private const uint PROCESS_CREATE_PROCESS = 0x0080;
    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;
    private const int TokenElevation = 20;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const nint PROC_THREAD_ATTRIBUTE_PARENT_PROCESS = 0x00020000;
    private const int ERROR_ELEVATION_REQUIRED = 740;

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEXW
    {
        public int cb;
        public nint lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2;
        public nint lpReserved2, hStdInput, hStdOutput, hStdError;
        public nint lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public nint hProcess, hThread;
        public int dwProcessId, dwThreadId;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenProcessToken(SafeProcessHandle process, uint access, out SafeAccessTokenHandle token);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetTokenInformation(SafeAccessTokenHandle token, int infoClass, out int info, int length, out int returned);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool InitializeProcThreadAttributeList(nint list, int count, int flags, ref nint size);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool UpdateProcThreadAttribute(nint list, uint flags, nint attribute, void* value,
        nint size, nint previous, nint returnSize);

    [LibraryImport("kernel32.dll")]
    private static partial void DeleteProcThreadAttributeList(nint list);

    [LibraryImport("kernel32.dll", EntryPoint = "CreateProcessW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool CreateProcessW(string? application, char* commandLine, nint processAttributes,
        nint threadAttributes, [MarshalAs(UnmanagedType.Bool)] bool inheritHandles, uint flags, nint environment,
        string? currentDirectory, ref STARTUPINFOEXW startupInfo, out PROCESS_INFORMATION processInfo);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
