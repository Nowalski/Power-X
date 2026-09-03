namespace PowerX.Core.Diagnostics.Crash;

public sealed record BugcheckInfo(int Code, string Name, string Meaning, string CommonCauses);

/// <summary>
/// Hand-curated plain-language descriptions of the common Windows stop codes. Not exhaustive —
/// an unknown code is reported honestly as "not in PowerX's catalogue". Sources: Microsoft
/// "Bug Check Code Reference" (learn.microsoft.com/windows-hardware/drivers/debugger).
/// </summary>
public static class BugcheckCatalog
{
    public static bool TryGet(int code, out BugcheckInfo info) => Map.TryGetValue(code, out info!);

    /// <summary>Format a raw stop code as "NAME (0xXX)" or just "0xXX" when unknown.</summary>
    public static string Describe(int code) =>
        Map.TryGetValue(code, out var i) ? $"{i.Name} (0x{code:X})" : $"stop 0x{code:X}";

    private static readonly Dictionary<int, BugcheckInfo> Map = new BugcheckInfo[]
    {
        new(0x0A, "IRQL_NOT_LESS_OR_EQUAL", "A driver accessed pageable (or invalid) memory at too high an interrupt level.", "A faulty or mismatched device driver; occasionally bad RAM."),
        new(0x1A, "MEMORY_MANAGEMENT", "The memory manager hit an inconsistency it can't recover from.", "Bad RAM, a failing disk, or a driver corrupting memory."),
        new(0x1E, "KMODE_EXCEPTION_NOT_HANDLED", "A kernel-mode program raised an exception the system couldn't catch.", "A driver bug; parameter 1 is the exception code (e.g. 0xC0000005 = access violation)."),
        new(0x3B, "SYSTEM_SERVICE_EXCEPTION", "An exception happened while executing a system call on behalf of a program.", "A driver or system file; often graphics or security software."),
        new(0x50, "PAGE_FAULT_IN_NONPAGED_AREA", "Something referenced memory that isn't valid, in an area that can't be paged.", "Bad RAM, a corrupt page file, or a driver. Parameter 1 is the bad address."),
        new(0x7E, "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED", "A system thread raised an exception nobody handled.", "A driver bug; the message usually names the failing driver file."),
        new(0x7F, "UNEXPECTED_KERNEL_MODE_TRAP", "The CPU raised a trap the kernel didn't expect (e.g. double fault).", "Bad or overclocked hardware (RAM / CPU), or a serious driver fault."),
        new(0x9F, "DRIVER_POWER_STATE_FAILURE", "A driver didn't complete a power transition (sleep / resume / shutdown) in time.", "A driver, very often a network, storage or USB one."),
        new(0xC2, "BAD_POOL_CALLER", "A driver made an illegal memory-pool request.", "A driver bug."),
        new(0xC4, "DRIVER_VERIFIER_DETECTED_VIOLATION", "Driver Verifier caught a driver breaking the rules.", "The driver named in the dump. You only see this if you turned Driver Verifier on."),
        new(0xC5, "DRIVER_CORRUPTED_EXPOOL", "The kernel pool was corrupted, caught at a raised IRQL.", "A driver writing out of bounds; sometimes bad RAM."),
        new(0xCA, "PNP_DETECTED_FATAL_ERROR", "The Plug-and-Play manager hit a fatal error.", "A driver mishandling PnP; occasionally failing hardware."),
        new(0xD1, "DRIVER_IRQL_NOT_LESS_OR_EQUAL", "A driver accessed pageable memory at too high an IRQL.", "A specific driver. The message usually names the file."),
        new(0xEF, "CRITICAL_PROCESS_DIED", "A process Windows can't run without (e.g. csrss, wininit) ended.", "System file corruption, aggressive 'debloat' tools, malware, or a bad update."),
        new(0xF4, "CRITICAL_OBJECT_TERMINATION", "A critical system process or thread terminated unexpectedly.", "A failing disk, corrupt system files, or a driver."),
        new(0x101, "CLOCK_WATCHDOG_TIMEOUT", "A CPU core stopped responding to the clock interrupt.", "Unstable CPU/RAM overclock, bad power delivery, or a firmware bug."),
        new(0x109, "CRITICAL_STRUCTURE_CORRUPTION", "The kernel found one of its own critical structures modified.", "A driver writing out of bounds, bad RAM, or tampering / anti-cheat conflicts."),
        new(0x116, "VIDEO_TDR_ERROR", "The display driver was reset because it stopped responding, and couldn't recover.", "GPU driver, an unstable GPU overclock, overheating, or a failing card."),
        new(0x117, "VIDEO_TDR_TIMEOUT_DETECTED", "The display driver stopped responding and Windows tried to reset it.", "GPU driver or GPU load / thermal / power issues."),
        new(0x124, "WHEA_UNCORRECTABLE_ERROR", "The hardware itself reported an uncorrectable error to Windows.", "A hardware fault: CPU, RAM, motherboard, PSU or an unstable overclock. Rarely software."),
        new(0x133, "DPC_WATCHDOG_VIOLATION", "A deferred procedure call ran too long, or the system spent too long at high IRQL.", "An out-of-date driver (classically an old SSD firmware or storage driver)."),
        new(0x139, "KERNEL_SECURITY_CHECK_FAILURE", "A kernel structure failed a security / integrity check.", "A driver bug, incompatible software, or memory corruption."),
        new(0x1000007E, "SYSTEM_THREAD_EXCEPTION_NOT_HANDLED_M", "Same as 0x7E: a system thread raised an unhandled exception.", "A driver bug."),
        new(0x1000008E, "KERNEL_MODE_EXCEPTION_NOT_HANDLED_M", "Same as 0x8E: a kernel exception nobody handled (older systems).", "A driver, service, or bad RAM."),
    }.ToDictionary(b => b.Code);
}
