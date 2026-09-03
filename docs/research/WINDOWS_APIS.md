# Windows APIs, telemetry & control surface

Preference order: **documented Win32 then documented perf counters (PDH) then WMI/CIM (only where it's the sane source) then NT APIs (justified, tested) then kernel driver (never, in-box)**.

## CPU

| Need | API | Status | Notes |
|---|---|---|---|
| Total CPU % | `GetSystemTimes` (idle/kernel/user, delta) | **in use** | kernel includes idle; busy = k+u-idle |
| Per-logical-processor % | `NtQuerySystemInformation(SystemProcessorPerformanceInformation=8)` | **in use** | array of `SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION`; also gives DPC/interrupt time |
| Topology (cores, SMT, cache, groups) | `GetLogicalProcessorInformationEx` | planned | needed for P/E-core + NUMA + socket display |
| Hybrid P/E core class | `GetSystemCpuSetInformation` (EfficiencyClass) | planned | class 0 = E, higher = P; capability-detect |
| Base/max clock | `CallNtPowerInformation(ProcessorInformation)` / `PROCESSOR_POWER_INFORMATION` | planned | current MHz is throttled estimate, label as approximate |
| Frequency %, C-states | PDH `\Processor Information(*)\% Processor Performance` | option | |

## Memory

| Need | API | Status |
|---|---|---|
| Physical total/avail/load | `GlobalMemoryStatusEx` | **in use** |
| Commit, limit, pool paged/nonpaged, cache, page size, handle/proc/thread counts | `GetPerformanceInfo` (psapi) | **in use** |
| Standby / modified / free list breakdown | `NtQuerySystemInformation(SystemMemoryListInformation=80)` | planned (RAMMap-style) |
| Per-process working set detail | `GetProcessMemoryInfo` / `QueryWorkingSetEx` | planned (inspector) |

## Processes

| Need | API | Status |
|---|---|---|
| Full enumeration + CPU/IO/mem/threads/handles in one shot | `NtQuerySystemInformation(SystemProcessInformation=5)` | **in use** |
| Image path (non-elevated) | `QueryFullProcessImageName` w/ `PROCESS_QUERY_LIMITED_INFORMATION` | wired, not yet surfaced |
| Command line | `NtQueryInformationProcess(ProcessCommandLineInformation=60)` (Win8.1+) else PEB read | planned |
| Owner / integrity level | `OpenProcessToken` + `GetTokenInformation(TokenUser / TokenIntegrityLevel)` | planned |
| Signature | `WinVerifyTrust` + `CryptQueryObject`; catalog via `CryptCATAdminCalcHashFromFileHandle` | planned |
| Modules | `EnumProcessModulesEx` / Toolhelp `Module32First` | planned |
| Handles | `NtQuerySystemInformation(SystemExtendedHandleInformation=64)` + `NtQueryObject` | planned (expert) |
| Threads + start addr | Toolhelp `Thread32First`; `NtQueryInformationThread` | planned |
| Control | `OpenProcess` + `TerminateProcess` / `NtSuspendProcess` / `NtResumeProcess` / `SetPriorityClass` / `SetProcessAffinityMask` / `SetProcessInformation(ProcessPowerThrottling)` for Efficiency mode | planned (M2) |

## GPU
- `IDXGIFactory`/`IDXGIAdapter3::QueryVideoMemoryInfo`, dedicated/shared VRAM, budget. **Documented.**
- Per-engine utilisation: PDH `\GPU Engine(*)\Utilization Percentage`, `\GPU Process Memory(*)`, `\GPU Adapter Memory(*)`. **Documented, this is what Task Manager uses.**
- Temp/power/clocks: **vendor only** (NVML, ADLX, IGCL). Optional provider modules, capability-detected. Never invent.

## Storage
- `\PhysicalDisk(*)` and `\LogicalDisk(*)` PDH counters (active time, read/write bytes, queue, latency).
- `DeviceIoControl(IOCTL_STORAGE_QUERY_PROPERTY)`, bus type, model, SSD/HDD, TRIM.
- SMART / disk health: `IOCTL_STORAGE_PREDICT_FAILURE`, or `MSStorageDriver_FailurePredict*` WMI. Label reliability.

## Network
- `GetIfTable2` / `GetIfEntry2`, per-interface octets, link speed, oper status (delta for rate).
- `GetExtendedTcpTable` / `GetExtendedUdpTable` (`*_OWNER_PID`), endpoints + owning PID (TCPView).
- `GetAdaptersAddresses`, addressing, DNS, gateway.
- Per-connection byte rates: ETW `Microsoft-Windows-Kernel-Network` (needs a session), later.

## Sensors / power / battery
- Battery: `GetSystemPowerStatus`, `CallNtPowerInformation(SystemBatteryState)`.
- Energy estimation: `CallNtPowerInformation` / process energy via `SystemProcessInformation` power fields + EnergyEstimationEngine counters.
- Temps: **no reliable in-box CPU/GPU temp API.** `MSAcpi_ThermalZoneTemperature` (WMI) is ACPI thermal zones, often just one, sometimes bogus. Treat as `Approximate` at best; prefer vendor provider modules.

## ETW (later)
`Microsoft-Windows-Kernel-Process`, `-Kernel-Network`, `-Kernel-Disk`, `-Kernel-File`, `TCPIP`. Real-time session via `TraceEvent`. Gives Process-Monitor-class file/registry/network activity and accurate per-process disk. Cost: a trace session + parsing budget, introduce deliberately.

## Registry
`Microsoft.Win32.Registry` for HKCU tweaks (no elevation). HKLM tweaks go through the elevated broker. All tweak keys documented in `TWEAK_CATALOG.md`, no bare constants.
