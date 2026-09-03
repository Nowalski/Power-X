using System.Diagnostics;
using System.Runtime.InteropServices;
using PowerX.Core.Interop;

namespace PowerX.Core.Processes;

public sealed record ProcessDetails
{
    public required int Pid { get; init; }
    public string? ImagePath { get; init; }
    public string? Description { get; init; }
    public string? Company { get; init; }
    public string? Version { get; init; }
    public bool? IsElevated { get; init; }
    public string IntegrityLevel { get; init; } = "";
}

/// <summary>
/// Best-effort per-process enrichment that needs a handle (path, version info, token).
/// Cheap enough to call on demand (inspector open, context menu); cache by pid+start time.
/// </summary>
public static class ProcessDetailsProvider
{
    public static ProcessDetails Resolve(int pid)
    {
        string? path = QueryImagePath(pid);
        string? desc = null, company = null, version = null;

        if (path is not null && File.Exists(path))
        {
            try
            {
                var fi = FileVersionInfo.GetVersionInfo(path);
                desc = Clean(fi.FileDescription);
                company = Clean(fi.CompanyName);
                version = Clean(fi.ProductVersion) ?? Clean(fi.FileVersion);
            }
            catch (Exception)
            {
                // unreadable version resource — leave nulls
            }
        }

        var (elevated, integrity) = QueryToken(pid);

        return new ProcessDetails
        {
            Pid = pid,
            ImagePath = path,
            Description = desc,
            Company = company,
            Version = version,
            IsElevated = elevated,
            IntegrityLevel = integrity,
        };
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    /// <summary>Just the executable path — a single query, no disk read. Safe to call on the UI thread.</summary>
    public static string? ImagePath(int pid) => QueryImagePath(pid);

    private static string? QueryImagePath(int pid)
    {
        nint h = ProcessNative.OpenProcess(ProcessNative.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h == 0) return null;
        try
        {
            Span<char> buf = stackalloc char[1024];
            uint size = (uint)buf.Length;
            return Kernel32.QueryFullProcessImageNameW(h, 0, buf, ref size)
                ? new string(buf[..(int)size])
                : null;
        }
        finally
        {
            ProcessNative.CloseHandle(h);
        }
    }

    /// <summary>Loaded modules (DLLs) for a process. Requires PROCESS_VM_READ — may be empty for protected processes.</summary>
    public static IReadOnlyList<string> Modules(int pid, int max = 400)
    {
        nint h = ProcessNative.OpenProcess(
            ProcessNative.PROCESS_QUERY_INFORMATION | ProcessNative.PROCESS_VM_READ, false, (uint)pid);
        if (h == 0) return [];
        try
        {
            var buffer = new nint[1024];
            if (!ProcessNative.EnumProcessModulesEx(h, buffer, (uint)(buffer.Length * nint.Size), out uint needed, ProcessNative.LIST_MODULES_ALL))
                return [];

            int count = Math.Min((int)(needed / nint.Size), Math.Min(buffer.Length, max));
            var result = new List<string>(count);
            Span<char> name = stackalloc char[512];
            for (int i = 0; i < count; i++)
            {
                uint len = ProcessNative.GetModuleFileNameExW(h, buffer[i], name, (uint)name.Length);
                if (len > 0) result.Add(new string(name[..(int)len]));
            }
            return result;
        }
        finally
        {
            ProcessNative.CloseHandle(h);
        }
    }

    private static (bool? elevated, string integrity) QueryToken(int pid)
    {
        nint h = ProcessNative.OpenProcess(ProcessNative.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h == 0) return (null, "");
        nint token = 0;
        try
        {
            if (!ProcessNative.OpenProcessToken(h, ProcessNative.TOKEN_QUERY, out token))
                return (null, "");

            bool? elevated = null;
            nint one = Marshal.AllocHGlobal(sizeof(uint));
            try
            {
                if (ProcessNative.GetTokenInformation(token, ProcessNative.TokenElevation, one, sizeof(uint), out _))
                    elevated = Marshal.ReadInt32(one) != 0;
            }
            finally
            {
                Marshal.FreeHGlobal(one);
            }

            return (elevated, QueryIntegrity(token));
        }
        finally
        {
            if (token != 0) ProcessNative.CloseHandle(token);
            ProcessNative.CloseHandle(h);
        }
    }

    // The mandatory-integrity SID's last sub-authority (RID) is the level:
    // 0x0000 untrusted · 0x1000 low · 0x2000 medium · 0x3000 high · 0x4000 system.
    private static string QueryIntegrity(nint token)
    {
        ProcessNative.GetTokenInformation(token, ProcessNative.TokenIntegrityLevel, 0, 0, out uint need);
        if (need == 0) return "";
        nint buf = Marshal.AllocHGlobal((int)need);
        try
        {
            if (!ProcessNative.GetTokenInformation(token, ProcessNative.TokenIntegrityLevel, buf, need, out _))
                return "";

            // TOKEN_MANDATORY_LABEL { SID_AND_ATTRIBUTES Label { PSID Sid; DWORD Attributes } }
            nint pSid = Marshal.ReadIntPtr(buf);
            if (pSid == 0) return "";
            int subCount = Marshal.ReadByte(pSid, 1);            // SID.SubAuthorityCount
            if (subCount == 0) return "";
            int rid = Marshal.ReadInt32(pSid, 8 + (subCount - 1) * 4);
            return rid switch
            {
                >= 0x4000 => "System",
                >= 0x3000 => "High",
                >= 0x2000 => "Medium",
                >= 0x1000 => "Low",
                _ => "Untrusted",
            };
        }
        catch (Exception)
        {
            return "";
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }
}
