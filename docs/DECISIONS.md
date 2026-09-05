# Decision log

Running record of significant choices. Newest first. Each entry: context → decision → why → status.

---

## D-033 — Audit: disk-cleanup scan parallelized; two duplicate-COM-walk costs found and flagged
**Date** 2026-09-05
**Decision** A solo follow-up audit pass (batching/performance, plus a fresh look for missing features, polish, and new ideas), continuing straight on from D-032.
- **`ToolsPage`'s disk-cleanup scan measured its 13 independent cleanup targets (temp folders, caches, logs, dumps) one after another inside a single `Task.Run`.** Same shape as every other fix in D-030 through D-032: no shared state between targets, so they scan in parallel now (`Parallel.ForEach` in place of `foreach`). Measured on the dev machine: 698ms sequential to 377ms concurrent for the same 13 targets and identical sizes/counts.
- **Two real, verified, but *not* fixed this pass — each needs a bigger decision than an audit-pass change:**
  - `DriverInventory.Read()`'s single `Win32_PnPSignedDriver` WMI query measured **~1.0-1.7s** elevated on its own (already flagged in D-032; re-confirmed here). Inherent to the WMI class itself (both `System.Management` and `Microsoft.Management.Infrastructure` are equally slow against it), not a coding bug. A real fix means replacing WMI with SetupAPI/`CM_Get_*` native device enumeration.
  - **`StartupProvider.Enumerate()` (used by the Startup page and `HealthCheck`'s startup check) and `TaskInventory.Enumerate()` (used by the Scheduled tasks page and `HealthCheck`'s tasks check) each do their own independent full recursive Task Scheduler COM walk of the same ~260 tasks** — `StartupProvider` calls `ScheduledTasks.Enumerate()` (filtered to logon/boot triggers) purely to find startup-relevant tasks, while `TaskInventory.Enumerate()` separately walks every task in full detail (and already computes each task's trigger types as part of that walk — including whether it fires "at boot" or "at logon", exactly the information `ScheduledTasks` recomputes from scratch). Measured on the dev machine: `TaskInventory.Enumerate()` ~1.7s, `StartupProvider.Enumerate()` (dominated by its internal `ScheduledTasks.Enumerate()` call) ~1.3s — two near-full-second-plus COM walks of the same tree, on two different call paths, whenever both Startup and Scheduled Tasks data are wanted together (which is exactly what `HealthCheck.ScanAsync` does, in its `CheckStartup` and `CheckScheduledTasks` checks). **Not fixed here** because the honest options both carry real risk for an audit-pass change: (a) have `ScheduledTasks`'s startup-subset filter `TaskInventory`'s already-collected trigger data instead of re-walking, which removes the duplicate work only when both are read through one call path, not when either is used alone (and makes standalone `StartupProvider.Enumerate()` slightly *slower*, since `TaskInventory`'s per-task read does more than `ScheduledTasks`'s filtered one); or (b) a short-TTL shared cache in front of the Task Scheduler walk, which needs a real invalidation story (a user who just toggled a task's enabled state must see that change immediately, not a cached stale one) that is more design than an audit pass should decide unilaterally. Either is a legitimate follow-up; recorded here rather than rushed.
- **Missing/incomplete features, cross-checked against `docs/research/ROADMAP_IDEAS.md`** (nothing here is new — restating what's still open there so it's visible from the decision log too): #6 per-process drill-down parity (handles/modules/threads/token groups, the largest remaining item), #7 memory drill-down (RAMMap-lite), #8 a unified resource timeline correlating CPU/mem/disk/net/GPU with the change log, #9 winget-backed app updates, #11 hardware-aware "Potato mode" auto-suggest (still Effort S, still not built — and now has an obvious home: a `HealthCheck` recommendation rather than a standalone feature, since that page exists now and #11 predates it).
- **Polish observed, not acted on**: nothing rising to the level of a decision-log item this pass — no correctness bugs, no confusing UI states found in the pages read.
**Status** Active (0.1.14). 126 tests (unchanged — the fix has no new pure-logic branch to unit-test; verified by direct before/after timing instead, same method as D-032's `SystemReport`/`EventLogBrowser` fixes).

---

## D-032 — Two-agent audit: SystemReport/EventLogBrowser batching, a real disk-temperature bug
**Date** 2026-09-05
**Decision** Ran two parallel audits (each in its own git worktree, non-overlapping file scopes, merged after independent verification) following up on D-031: one general performance/batching sweep, one specifically hunting for more real temperature data.
- **`SystemReport.BuildMarkdown`'s seven sections now run concurrently** (System/Hardware/Storage/Applied tweaks/Recent changes/Event-log errors/Crashes), same pattern as `HealthCheck.ScanAsync` — each section extracted into its own function, run via `Task.Run`, results stitched back in a **fixed order** (not completion order — a new test, `BuildMarkdown_keeps_sections_in_a_fixed_order_even_though_they_run_concurrently`, pins this down). Measured on the dev machine: ~232ms sequential to ~120ms concurrent. Already always called via `Task.Run` at both its call sites (Settings page, CLI), so this was wasted wall-clock time, not a UI freeze.
- **`EventLogBrowser.Read` and `SystemReport.EventErrors`** each read the Application/System(/Setup) event logs one after another; both now read every log concurrently and merge the per-log results after (safe: every dictionary key is already log-qualified, so two logs' entries can never collide). `EventLogBrowser`: ~90ms to ~55ms. This directly speeds up `HealthCheck.CheckEventLog`, `EventLogPage`, and `powerx events`.
- **`CrashScanner.Scan` was tried the same way and reverted.** WER reads are near-free on the dev machine (a handful of report folders) while the event-log read already dominates, so running them concurrently only added a thread-pool hop with nothing to overlap against (measured slower: ~74ms vs ~54ms sequential). Left sequential with a comment explaining why, so a future pass doesn't retry blindly. This is the useful negative result of the audit: not every "these look independent" pair is worth parallelizing — measure, don't assume.
- **Flagged, not fixed**: `DriverInventory.Read`'s single `Win32_PnPSignedDriver` WMI query measured **~1.0-1.2 seconds** elevated on its own (confirmed via both `Get-CimInstance` and `Get-WmiObject`, so it's inherent to the WMI class, not a coding bug) — almost certainly the long pole inside `HealthCheck.ScanAsync`'s ~1.5s total. A real fix means replacing the query mechanism entirely (SetupAPI/`CM_Get_*` native device enumeration instead of WMI), which is a bigger, riskier rewrite than an audit pass should do opportunistically. Worth a dedicated follow-up.
- **Real bug found and fixed**: `StorageInfo.PhysicalDisks()` matched each disk to its `MSFT_StorageReliabilityCounter` through a `Dictionary<FriendlyName, …>` side table. Two identically-modeled drives (confirmed on the dev machine: two Samsung SSD 990 PRO 4TB NVMe drives) share the same `FriendlyName`, so the second disk's reliability data silently overwrote the first's — both drives showed the same temperature. Fixed by reading each disk's own reliability counters directly off its live WMI association (`disk.GetRelated("MSFT_StorageReliabilityCounter")`) inside the same enumeration loop, instead of joining through a name-keyed table built from a separate query. Verified before (37.0°C twice) and after (44.0°C / 37.0°C, matching a raw WMI probe) via `powerx temps`. This bug predates the Temperatures page — it also silently affected the Health check's disk-health check and the Storage explorer wherever a disk's own temperature was shown next to another with the same model name.
- **`ThermalInfo` gained a second-attempt ACPI source**: `Win32_PerfFormattedData_Counters_ThermalZoneInformation`, a separate WMI provider over the same underlying ACPI thermal-zone data as `MSAcpi_ThermalZoneTemperature` (whole Kelvin, not tenths — confirmed against Microsoft's published counter description), tried only when the primary class returns zero instances. Also empty on the dev machine (confirmed), so this is unverified-but-harmless here; it may catch a machine whose firmware/driver stack populates one provider but not the other. The existing "no CPU/system sensor" messaging is unchanged and still fires when both are empty.
- **Investigated and declined**: `MSFT_StorageReliabilityCounter.TemperatureMax` (returns the UInt8 sentinel `255` on both drives here, not a real threshold, so not shown), battery temperature (`BatteryStatus`/`BatteryTemperature` WMI classes aren't even registered in the CIM schema on this battery-less desktop, so this could not be verified at all and was left out entirely rather than shipped unverified), SATA-specific temperature handling (no SATA drive on the dev machine to verify against; the existing bus-agnostic `MSFT_PhysicalDisk` path is Microsoft's documented unified replacement for raw-SMART parsing, so left as-is on reasoning rather than verified behavior).
- **The `nvidia-smi` question, decided here rather than left to code**: `nvidia-smi.exe` (ships with the NVIDIA driver, not something PowerX installs) works and returns real GPU temperature with no elevation needed — confirmed on the dev machine (39°C, correct). **Declined anyway.** It is mechanically similar to `CommandRunner`'s existing shell-outs to `sfc`/`dism`/`chkdsk`/`powercfg`, but those are in-box Windows tools; `nvidia-smi` is NVIDIA's own vendor tool, functionally equivalent to reaching into NVAPI, which is exactly the line `GpuMetrics`'s doc comment and this log have drawn on purpose more than once. It would also only ever help NVIDIA users: there is no AMD equivalent CLI for consumer/integrated Radeon GPUs on this machine (AMD's `amd-smi` targets Instinct/ROCm datacenter cards). Showing GPU temperature for one vendor and not others needs its own decision if it ever happens, not an audit-pass add. No code for this was added anywhere.
**Status** Active (0.1.13). 126 tests (was 125). Both audits verified independently (own build+test+elevated-CLI runs) before merging, on top of each sub-agent's own verification.

---

## D-031 — Multi-GPU breakdown, network adapter enable/disable, Temperatures page, parallel health scan
**Date** 2026-09-05
**Decision** Four items from a follow-up conversation about the Network/GPU pages and general responsiveness.
- **GPU page now shows every real adapter, not just one blended reading.** `GpuMetricsProvider` previously summed `\GPU Engine` counter values across every adapter by engine type alone, discarding the LUID embedded in each instance name (`…luid_0xHHHHHHHH_0xLLLLLLLL…`) — on a machine with an integrated and a discrete GPU, both cards' usage silently blended into one number, and the static "Adapter" spec card only ever named the highest-VRAM card. Fixed by parsing each counter instance's LUID (`GpuMetricsProvider.ParseLuid`, unit-tested against real captured instance names) and grouping by adapter. Adapter identity/VRAM comes from DXGI (`Interop/Dxgi.cs`, `IDXGIFactory1::EnumAdapters1` + `IDXGIAdapter1::GetDesc1`) rather than WMI's `Win32_VideoController`, which has no LUID and whose `AdapterRAM` saturates at 4 GB on anything with more VRAM (the previous registry-`qwMemorySize` workaround for that is no longer needed). Software/remote adapters (DXGI's own `Flags`) and virtual-adapter LUID duplicates of the same physical card (seen in testing behind a VR compositor driver) are filtered out. GPU page gained an adapter picker (Combined / each GPU by name, defaulting to the highest-VRAM card, matching what the page always showed before this existed) plus an always-visible per-GPU card row. `TelemetryHub` keeps one history ring per adapter LUID.
- **Network page can now enable/disable an adapter.** `NetworkAdapterControl` (`Telemetry/`) reads and toggles `InterfaceAdminStatus` via `MSFT_NetAdapter` (`root\StandardCimv2`) — the same class `Enable-NetAdapter`/`Disable-NetAdapter` use. Deliberately **not** `System.Management` (classic WMI): querying `MSFT_NetAdapter` through it silently returns the base `CIM_NetworkPort` shape with none of this class's own properties populated (no error — confirmed by direct comparison against `Get-NetAdapter`'s real values), because it is a native CIM/MI class and the legacy DCOM WMI bridge does not fully realize it. Needed the `Microsoft.Management.Infrastructure` package (the same client `Get-CimInstance`/`Invoke-CimMethod` use) instead. Shown as a separate "Network adapters" list sourced from this class rather than added to the live-throughput interface cards, because an administratively-disabled adapter disappears entirely from `NetworkInterface.GetAllNetworkInterfaces()` (confirmed empirically) — putting the toggle there would strand a disabled adapter with no way back from the same page. Disabling asks for confirmation; enabling does not.
- **New Temperatures page** (`ThermalInfo`, Core) — ACPI thermal zones (`MSAcpi_ThermalZoneTemperature`, `root\WMI`) and per-disk sensors (already read by `StorageInfo.PhysicalDisks`). On most desktops the ACPI class reports no instances at all (motherboard-firmware-dependent, confirmed on the dev machine); CPU and GPU temperature are not exposed by any in-box Windows API at all, and — same reasoning as GPU engine/clock data (see the `GpuMetrics` doc comment) — PowerX does not reach for a vendor SDK (NVAPI, ADL) or a kernel driver to get them, so the page says so plainly instead of showing nothing unexplained. `powerx temps`.
- **`HealthCheck.ScanAsync`'s eleven checks now run concurrently** instead of one after another — they share no state (each returns its own list; a "Safe" wrapper still isolates one check's exception from the rest). Measured on the dev machine: 4.2s sequential → 1.5s concurrent for the same 7 findings/score. This was the first fix this pass looked for after 0.1.11's "why does X take a bit" audit turned up a real one; the same shape (several independent slow calls made one after another for no dependency reason) is worth checking for again in `SystemReport.BuildMarkdown`, which was not changed this pass because it is already off the UI thread everywhere it is called from and so does not have the freeze problem, only a slower-than-it-needs-to-be one.
**Status** Active (0.1.12). 125 tests (was 119). Verified live against real hardware: a dev machine with an NVIDIA RTX 5080 and an AMD integrated GPU, and a network adapter set with a mix of enabled/disabled entries.

---

## D-030 — Audit: network throttled to half rate, Health check froze the UI
**Date** 2026-09-04
**Decision** Two fixes prompted by a report that the Monitor pages, network in particular, "take a bit to show something".
- **`TelemetryHub` sampled network on every other tick** (`_tick % 2 == 0`), a P2 fix from an earlier audit pass meant to save the ~10-20 ms `NetworkInterface.GetAllNetworkInterfaces()` costs. That sampling already runs entirely on the background loop thread, never the UI thread, so the saving bought nothing visible and cost real responsiveness: the Network page's down/up rate stuck at its old value for a full extra second between updates, and read blank for up to 2 seconds after the app opened (nothing to show until the first network sample landed). Reverted to sampling network every tick like CPU/memory/GPU; `Commit` and `SampleOnce` simplified now that a network sample is always present.
- **`HealthCheck.ScanAsync` ran fully synchronously** — fourteen checks including a `powercfg /batteryreport` process spawn, WMI, COM firewall/task enumeration, and a 7-day event-log read — with no `Task.Run` anywhere in the non-`deep` path. `async Task<HealthReport>` with no `await` reached before the checks run just executes them on the caller's thread, and the Health check page calls it directly from `OnNavigatedTo`/a button click, so opening the page (it also auto-scans on first load) froze the whole app for a second or more. Fixed by wrapping the check batch in `await Task.Run(...)` inside `HealthCheck.ScanAsync` itself, so every caller (the page and `powerx doctor`) benefits without having to remember to offload it.
**Status** Active (0.1.11). Verified: build clean, 119 tests, elevated smoke launch on the Network page. No behaviour change to what is reported, only when and how fast.

---

## D-029 — Health check, broken-startup cleanup, "explain this process", CSV export
**Date** 2026-09-04
**Decision** 0.1.10, four features that round off the recent inventory work.
- **Health check** (new page, nav right under Home). `HealthCheck.ScanAsync(deep)` runs fourteen checks against providers PowerX already had — pending restart, antivirus status, firewall profile/rules, disk space and health, broken startup entries, boot degradation, enabled telemetry tasks, driver age, battery wear, event-log criticals, recent crashes, unapplied recommended tweaks — and turns them into one prioritised list (High/Medium/Low), each item pointing at the page that handles it. It changes nothing itself. `deep` additionally runs the WinSxS analysis (slow, off by default). A 0-100 score is cosmetic, not a target to chase. `powerx doctor [--deep]`.
- **Broken startup entries**. `StartupProvider` now flags a Run/RunMachine entry as `Broken` when its command names a specific path (drive letter or UNC) that does not exist — almost always left behind by an uninstalled app. `CanRemove`/`Remove` now cover this case too (previously RunOnce only); `Remove` was generalised to operate on either the Run or RunOnce key and stash the backup under a key-qualified name. A bare command name PowerX did not resolve (e.g. `rundll32.exe`) is deliberately *not* flagged — no evidence it is actually broken.
- **"Explain this process"**. `ProcessKnowledge.Explain(name, path, company)` — ~45 curated notes for common Windows/third-party processes, a Microsoft-signed-and-in-System32 heuristic for the rest, and an honest "not in the list, check its hash" for anything else. Shown as the first row of the Process inspector's Overview tab. Never a verdict on a specific running instance — only what the name normally means.
- **CSV export**. A shared `Services.CsvExport` helper and an "Export CSV" button on Drivers, Firewall, Scheduled tasks and Event log.
**Status** Active (0.1.10). 119 tests.

## D-028 — Scheduled-tasks curator, drivers, firewall viewer, event log, config export, per-process net, delayed startup
**Date** 2026-09-04
**Decision** 0.1.7, seven features. Six read-only, one (config import) applies tweaks behind a preview.
- **Scheduled tasks** page (`TaskInventory` + `ScheduledTaskCatalog`, ~40 curated stances matched by path substring: Telemetry / Optional / KeepSystem / Unreviewed). Toggling reuses `ScheduledTasks.SetEnabled` (reversible, never deletes). KeepSystem tasks can't be toggled. `powerx tasks`.
- **Drivers** page (`DriverInventory`, `Win32_PnPSignedDriver`). Flags drivers ≥3y (`Old`) / ≥5y (`VeryOld`) and unsigned; Microsoft inbox drivers are never flagged (deliberately old). Never installs anything. `powerx drivers`.
- **Firewall** page (`FirewallRules`, `HNetCfg.FwPolicy2` COM, **read-only**). Profile on/off per profile + all rules, with a `WorthReviewing` flag for an enabled inbound-allow rule that opens a port for any program on the public profile and has no owner SID (i.e. an admin-punched hole, not a Store-app rule). `powerx firewall`.
- **Event log** page (`EventLogBrowser`, `EventLogReader` over Application/System/Setup, grouped by source+id, ~25 hand-written plain-language notes for common ids). `powerx events`.
- **Config export/import** (`ConfigBundleService`, Settings card). Exports applied tweak ids as `powerx.config/1` JSON (no machine detail); import shows a per-item plan and applies the tweak half via `ApplyMany` behind a confirm. `powerx config export|import [--apply]`.
- **Per-process network** card on the Network page (`NetworkUsageEtw`). Private ETW real-time session on `Microsoft-Windows-Kernel-Network` via the `Microsoft.Diagnostics.Tracing.TraceEvent` package (new dep — the hand-rolled `EVENT_TRACE_LOGFILEW` marshalling was too crash-prone to ship untested; DIA/symbol/ETL-merge native extras are trimmed from publish). Needs elevation; card hidden if the session can't start. Page-local lifecycle.
- **Delayed startup**: the Startup page `…` menu on an eligible Run entry offers "Delay after sign-in" (30s–3min). `StartupDelay` creates a `\PowerX\Delayed - <name>` scheduled task with a delayed logon trigger and disables the original entry; undo removes the task and re-enables.
**Nav** now 22 items: Firewall under Monitor; Scheduled tasks + Drivers under Software; Event log under Optimize & fix.
**Status** Active (0.1.7). 112 tests.
**0.1.8** Storage explorer streams results per folder instead of measuring the whole tree before drawing anything; default root is the user profile.
**0.1.9** Fixed the per-process network card, which had never actually worked: `NetworkUsageEtw` was reading the PID and byte count via `TraceEvent.ProcessID`/`PayloadByName`, neither of which resolves for `Microsoft-Windows-Kernel-Network`'s send/recv events (ProcessID is the kernel logging context, almost always 4; the provider's manifest fields do not surface through the generic parser). Fixed to read both as the first two UInt32 fields of the raw event payload; verified against live traffic. Also: TaskInventory/StartupDelay/FirewallRules release the COM collection objects they enumerate, and a delayed-startup logon trigger is scoped to its own user.

## D-027 — Config-drift snapshots, storage explorer, battery, pending-reboot, WinSxS
**Date** 2026-09-04
**Decision** 0.1.5, four read-only features plus an audit pass.
- **`SystemSnapshot`** (Core): a daily background JSON snapshot of startup entries, scheduled tasks, auto-start services, installed programs, `Win32_PnPSignedDriver` drivers and applied tweaks under `%LOCALAPPDATA%\PowerX\snapshots` (keep 40, prune oldest). `Diff(from, to)` → added/removed/changed. New **"What changed"** page (nav, above Change history — which stays PowerX's own action log) with two snapshot pickers; `powerx changes [--snapshot]`. Nothing leaves the machine.
- **`FolderSizer`** (Core): sizes the immediate children of a folder, sub-folders measured recursively in parallel, `AttributesToSkip = ReparsePoint` so junctions/symlinks aren't followed or double-counted. New **"Storage explorer"** page (nav) with drill-down; `powerx storage <path>`.
- **`PendingReboot`** (Core): reads the documented CBS / Windows Update / `PendingFileRenameOperations` / computer-rename keys and says *why* a restart is owed. Tools page InfoBar; `powerx reboot`.
- **`ComponentStore`** (Core): `DISM /Online /Cleanup-Image /AnalyzeComponentStore` parse + `/StartComponentCleanup` (never `/ResetBase` — that permanently blocks uninstalling updates). Tools card.
- **`BatteryHealth`** (Core): wear %, cycle count and runtime from `powercfg /batteryreport /xml` + live state from `GetSystemPowerStatus`. Tools card, hidden on desktops; `powerx battery`.
- Startup boot card gained a 12-boot trend sparkline (`BootTimeline.Recent`).
- New `Interop/ShellLink` (`IShellLinkW`) so Startup-folder `.lnk` entries resolve to a target, publisher and boot-impact match.
**Audit pass (same release):** `UpdateInstaller.Launch` re-hashes the MSI against the manifest SHA-256 immediately before running it (closes the verified→executed gap for a file in a user-writable folder). `StartupProvider.ResolveExe` uses the `.exe`-boundary splitter for unquoted spaced paths. `HashLookup` never flags a falsy `KnownMalicious`. `SystemReport` scrubs the user/machine name on whole-word boundaries. `HashLookup` reuses one static `HttpClient`. `Defender` sorts Platform folders by parsed `Version`. `ReverseDns` skips IPv6 ULA + multicast. `NetworkPage` refreshes the interface throughput line in place. `ProcessesPage.Report` can't crash on a double dialog. CA1001 suppressed with a note. `Fmt.Rate` prints `0/s` not an em dash.
**Status** Active (0.1.5; hardened 0.1.6). 97 tests.

## D-026 — Startup impact from the Diagnostics-Performance log; audit fixes
**Date** 2026-09-04
**Decision** 0.1.4. `BootPerformance` (Core) reads `Microsoft-Windows-Diagnostics-Performance/Operational` events 100 (boot total, main-path time, degradation flag) and 101/102/103 (slow app/driver/service, with the added milliseconds). That log needs elevation to read; without it the result is empty, never an exception. The Startup page shows a boot-time card and a High/Medium/Low impact chip on matched entries. This is exactly Task Manager's "Startup impact" data source; PowerX shows the real numbers where Windows recorded them and says nothing where it did not (no fabricated per-app score).
**Audit pass fixes (same release):** `ReverseDns` now records that an address was *attempted* so a name with no PTR record is not re-queried on every 3-second refresh. `SystemReport` dropped a lookahead-heavy "serial" regex (ReDoS risk, and it scrubbed nothing the report actually emits) and put a 250 ms timeout on the remaining scrubs. A shared `Clip.SetText` retries the clipboard and never lets a transient `COMException` surface. `SecurityPage` guards every post-await UI write with `_onPage`. Small dead-code and disposal cleanups in `HashLookup`, `Defender`, `NetworkPage`.
**Status** Active (0.1.4). 83 tests.

## D-025 — Security page: surface Defender, never be the antivirus
**Date** 2026-09-03
**Decision** A "Security" page (nav, under Optimize & fix) + `powerx security` / `powerx hash`. It does three things and refuses to do more:
1. **Defender status** via its WMI provider (`root\Microsoft\Windows\Defender`, `MSFT_MpComputerStatus` + `MSFT_MpPreference`): running mode (Normal / Passive / EDR), real-time / cloud / behavior / tamper / network protection, PUA setting, definition version + age, last scan times, exclusion count. A red bar when there is no active real-time AV at all (`DefenderStatus.Unprotected`).
2. **Threat history** via `MSFT_MpThreat` + `MSFT_MpThreatDetection` joined on `ThreatID`: what Defender has already caught (name, severity, date, state, the file). Read what Windows recorded, same pattern as crash insights.
3. **Scan**: launches `MpCmdRun.exe -Scan -ScanType 1|2` (resolved from Program Files or the versioned Platform folder), streams output, cancel kills it + `-CancelScan`.
4. **Hash check** (`HashLookup`): SHA-256 of a file looked up against **CIRCL hashlookup** (`hashlookup.circl.lu`, free, open, no key, ~40B known files from NSRL and clean-software sources). Reports "known good (trust N/100)", "low trust", "known malicious" (`KnownMalicious` field), or "not catalogued, which proves nothing". Only the hash leaves the machine, over HTTPS, on an explicit click; results cached.
**Why** People asked for "malware scanning". A half-working AV that users trust *instead of* Defender is actively harmful, so PowerX will not build one: no signatures, no quarantine, no "you are clean" all-clear, no auto-removal. What is safe and useful is showing the protection that is already there, what it caught, and an open second-opinion hash lookup. VirusTotal / MalwareBazaar were considered but both now need an API key; CIRCL needs none. The "suspicious indicator" heuristic scan (Autoruns-style) was scoped out for now to avoid the app making accusations.
**Status** Active (0.1.2). `powerx help` markup-escape bug fixed along the way (a `[--flag]` in a description crashed Spectre). +6 tests.
**0.1.3** the "Check a file" Browse button used WinUI `FileOpenPicker`, which silently does nothing in an unpackaged elevated app. Replaced with the classic `GetOpenFileNameW` (comdlg32) in `App/Services/NativeFileDialog.cs`. The COMDLG filter is a double-null-terminated multi-string, so it is built by hand rather than left to the `LPWStr` marshaller.

## D-024 — `powerx report` and network deep-dive: read-only, redacted by default, no automatic lookups
**Date** 2026-09-03
**Decision** Two "Understand" features shipped in 0.1.1.
`SystemReport.BuildMarkdown(ReportOptions)` collects OS, hardware, storage/SMART, applied tweaks (with dates from the change log), recent change history, an event-log error summary (Application + System, Level 1-2, grouped by source+id) and a crash summary (via `CrashScanner`). **Redaction is on by default**: user name, machine name, MAC addresses and serial-looking strings are scrubbed from the final text. Every section is best-effort — a section that fails says so instead of failing the report. Surfaces: `powerx report [--out PATH] [--no-redact] [--print]` and a Settings button that shows the full text in a dialog before it is saved.
Network page: a **Listening ports** view (port, process, bound address, and whether it is reachable from the network — bound to something other than loopback), a connection-state summary, **opt-in reverse DNS** (`ReverseDns`, only after the user turns "Resolve names" on, cached, public routable addresses only — never private/loopback/link-local, never automatic), and a copy-to-clipboard for the connection list. `NetworkConnection` gained structured `LocalAddress`/`LocalPort`/`RemoteAddress`/`RemotePort`/`IsListening`/`Exposed`.
**Why** The support bundle is the highest-value item for the "Understand" pillar and for the project's own bug reports, but a report that leaks identifiers is worse than none. Reverse DNS answers "what is this program talking to", but doing it automatically would be a passive data-exfil pattern and generate constant DNS traffic. Same stance as D-017 (updater) and D-022 (crash insights): read what is there, never phone out without consent.
**Status** Active (0.1.1). 68 tests.

## D-023 — Distribution: a WiX MSI installer is the primary channel; portable exe secondary
**Date** 2026-09-03
**Decision** The main way to get PowerX is **`PowerX-Setup-<ver>-win-x64.msi`** (WiX 5, `installer/PowerX.wxs`, built by `installer/build.ps1`). One 53 MB file → per-machine install to `Program Files\PowerX`, a Start-menu shortcut and a desktop shortcut, Add/Remove Programs entry, in-place major upgrade (shared `UpgradeCode`), clean uninstall. The WinUI app stays **unpackaged and unchanged** — the MSI only lays down the self-contained publish folder (with the bundled VC++ runtime). WiX 5 (not 7 — v7 needs the paid OSMF EULA) as a `dotnet tool`, build-time only. A single-file self-contained `PowerX.exe` remains available as a no-install portable option but is not the recommended one (slow first launch, AV false positives).
**Why** The 500-file self-contained folder is the correct WinUI 3 unpackaged layout but reads as junk to users. MSIX would mean re-architecting storage/`ms-appx`/elevation. An MSI is one file, standard, reproducible in CI, and needs no app changes.
**Status** Active. Not code-signed yet → SmartScreen "unknown publisher". Elevated end-to-end install verified only by payload inspection + running the app from the extracted layout (the CI/dev shell can't elevate).

## D-022 — Crash insights: read what Windows recorded, never analyse dumps with a debugger
**Date** 2026-09-03
**Decision** `PowerX.Core/Diagnostics/Crash` reads the WER `Report.wer` store, the Application + System event logs (1000/1001/1002/1026, WER-SystemErrorReporting 1001, EventLog 6008), `Win32_ReliabilityRecords` (deferred), and — only when the user ticks the box, only elevated — the *metadata* streams of a user-mode minidump (`MinidumpReader`: header + directory + SystemInfo/ModuleList/Exception, every RVA bounds-checked, no memory streams, no pointer-following). It does **not** download debugging symbols, load a dump into `dbgeng`/`dbghelp`, or upload anything. `CrashScanner` produces `CrashInsight` records that keep *observed facts* separate from *likely causes*, tag a `CrashConfidence` (Insufficient/Low/Moderate/High), and list *what's missing* — it says "insufficient evidence", it does not guess. `BugcheckCatalog` is ~24 hand-curated stop codes. Kernel dumps are never parsed (needs a debugger engine + symbols). New first-party dependency `System.Diagnostics.EventLog`. Surfaces: `powerx crashes` and a "Crash insights" page under Optimize & fix.
**Why** The evidence Windows already keeps answers most "why did it crash" questions without symbols or a debugger. Doing more would mean a large dependency, a security surface (`dbgeng` loads plugins), and a symbol-server EULA — for diminishing returns. Design investigation: `docs/research/CRASH_DIAGNOSTICS.md`.
**Status** Active (v1). Fast-follow: minidump `ModuleList` fault attribution in the default (non-elevated) path where the dump is user-readable; `Win32_ReliabilityRecords` timeline.

## D-021 — Portable distribution: self-contained folder build, VC++ runtime bundled
**Date** 2026-09-03
**Decision** The "send a friend" build is `dotnet publish -c Release -r win-x64 --self-contained -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None` — a **folder** (not single-file). An `IncludeVcRuntime` MSBuild target copies `vcruntime140*`/`msvcp140*`/`concrt140` next to the exe from the VS redist dir (System32 fallback). Zipped inside one `PowerX/` folder with a `READ ME FIRST.txt`. Not code-signed yet (SmartScreen "Run anyway").
**Why** A self-contained WinUI 3 publish does **not** include the VC++ 2015-2022 runtime that native `Microsoft.UI.Xaml.dll` links against; on a clean PC the app dies silently during MainWindow XAML load. Single-file publish *works* but is fragile across machines (extraction to `%TEMP%\.net`, AV interference) — a friend's first report was exactly this failure mode. The 500-file folder is uglier but reliable, and the user only ever double-clicks one exe.
**Status** Active. `src/PowerX.App/PowerX.App.csproj` `IncludeVcRuntime` target. Revisit when the app is code-signed and an installer exists.

## D-020 — RunOnce startup entries: never a fake toggle
**Date** 2026-09-03
**Decision** `StartupProvider.SetEnabled` refuses RunOnce entries (returns a failure explaining they run once at next sign-in and aren't governed by `StartupApproved`); `CanToggle()` lets the UI disable the switch. `Remove(entry)` / `CanRemove(entry)` deletes a RunOnce value (stashing name+value+hive to `HKCU\SOFTWARE\PowerX\RemovedRunOnce` for manual recovery) — surfaced as a confirm-gated "Remove entry" in the Startup page's … menu.
**Why** Windows ignores `StartupApproved\Run` for `RunOnce`, so the previous code wrote a "disabled" marker that did nothing while the UI claimed success — a fake-success violation of the honesty rules. Deleting the value is the only real way to stop it; the backup keeps it recoverable.
**Status** Active.

## D-019 — Theme options: window material + accent, no UI-density toggle (yet)
**Date** 2026-09-02
**Decision** Settings ▸ Appearance offers theme (System/Light/Dark), **window material** (Mica / Acrylic / None-solid) and an **accent colour** from 8 presets. Material applies live via `Window.SystemBackdrop`; when "None", `ApplyBackdrop` paints the root panel with an opaque theme brush so the content area isn't left unpainted. Accent overrides all six `SystemAccentColor{Light,Dark}{1..3}` tint resources at launch (before first brush resolution) by lerping the base toward white/black; it takes effect on restart. No compact/comfortable density toggle — WinUI 3 has no supported compact metrics and hand-scaling every control is fragile.
**Why** Material + accent are the high-impact, low-risk parts of "theming". Density is deferred until there's a supported mechanism.
**Status** Active. `AppSettings.Backdrop` / `.Accent`.

## D-018 — Optimization profiles are visible tweak sets, never hidden scripts
**Date** 2026-09-02
**Decision** `OptimizationProfile` is `(Id, Name, Description, Tone, TweakIds[])`. Built-ins: `recommended`, `privacy`, `lowspec` ("Potato mode"), `gaming`, `restore` (empty list — special-cased to revert everything currently applied). Applying one always shows a preview diff (what will change vs. the live machine state), offers an optional restore point, then runs `TweakEngine.ApplyMany` as one transaction. A test asserts every referenced tweak resolves and is not `SecurityTradeoff`/`Destructive`, so no profile can ever weaken a security boundary. Same model drives `powerx profile list|show|apply`.
**Why** Prompt §§ safety: detect→show→apply→verify→log→undo. A profile is a convenience, not a black box; the user sees and confirms every change.
**Status** Active. `src/PowerX.Core/Tweaks/OptimizationProfile.cs`.

## D-017 — Update check: a version.json in the repo, no auto-download
**Date** 2026-09-02
**Decision** `version.json` at the repo root holds `{version, published, notes, url, minimumWindowsBuild, installerUrl, installerSha256, installerBytes}`. `UpdateChecker` fetches it from `raw.githubusercontent.com/Nowalski/Power-X/main/version.json`, compares to the running assembly version, and surfaces the result — a dismissible `InfoBar` in the app, `powerx update` in the CLI. Auto-check is once/day, opt-out in Settings, dismissed version not re-nagged.
**Updated (D-023):** when the manifest carries a hash-pinned installer (`installerUrl` on `github.com`/`objects.githubusercontent.com` over HTTPS, a 64-hex `installerSha256`, a positive `installerBytes`), the app offers **Download & install** — `UpdateInstaller` downloads to `%LOCALAPPDATA%\PowerX\update`, verifies size + SHA-256, refuses to run on any mismatch, then launches `msiexec /i` (self-elevating in-place upgrade) and exits. CLI: `powerx update --download`. Without those fields it still just opens the releases page. This is not "download a mystery binary" — the URL host is allow-listed, the hash is pinned in a manifest served over HTTPS from the project's own repo.
**Why** A passive link is safe but the folder-vs-MSI switch (D-023) made a proper in-app updater worth the small, well-bounded surface: allow-listed host + pinned hash + explicit user consent + verify-before-run.
**Status** Active. The check 404s until `version.json` lands on `main`; installer fields are empty until the first tagged release.

## D-015 — Registry-tweak "absent value" means default, not "Custom"
**Date** 2026-09-02
**Decision** `RegistryTweakOperation.Detect` treats a missing registry value as matching the Windows default (unless the tweak's *applied* state is itself "value absent"). Previously an absent value matched neither the concrete `AppliedValue` nor the concrete `DefaultValue` and reported `Custom`, which the UI left un-togglable.
**Why** Most Windows toggles ship with the value absent; the old behaviour made ~half the catalog appear stuck.
**Status** Active. Multi-value tweaks can still legitimately be `Custom` when partially set.

## D-016 — Debloat removes for all users + deprovisions (we run elevated)
**Decision** Curated non-`KeepSystem` entries use `RemovePackageAsync(RemoveForAllUsers)` + `DeprovisionPackageForAllUsersAsync`. Un-catalogued packages fall back to the system-signature gate. A small `KeepSystem` list (Store, Security, shell frameworks, Terminal/Notepad/Paint/Snipping) is never removable.
**Why** Inbox consumer apps are system-signed; per-user removal alone left them greyed out and they'd return. This is the DISM-equivalent path, done safely per-package with confirmation.
**Status** Active.

## D-014 — App runs elevated (manifest `requireAdministrator`)
**Date** 2026-09-02
**Decision** `app.manifest` requests `requireAdministrator`. PowerX manages system state (process control, services, HKLM tweaks) — a split broker is still the long-term goal (D-ARCH) but until it exists the app runs elevated so every feature works. Every launch shows one UAC prompt.
**Status** Active. `ProcessActions` / `QuickActions` still surface access-denied gracefully for protected processes.

## D-013 — Migrated to .NET 10 (SDK 10.0.400) once installed on the build host
**Date** 2026-09-02
**Decision** All projects target `net10.0-windows` (`net10.0-windows10.0.19041.0` for `PowerX.App`); `Microsoft.Extensions.*` → 10.0.0; `Microsoft.WindowsAppSDK` → 1.8.250907003. `global.json` pins 10.0.400 with `rollForward: latestFeature`.
**Why** D-004 always planned this; .NET 10 is the intended target from the brief (§35). Build + 14 tests + WinUI x64 Release all green on 10.
**Note for local dev:** the machine also has an SDK-less `C:\Program Files (x86)\dotnet` that can win PATH order and produce "No .NET SDKs were found". Put `C:\Program Files\dotnet` first on PATH (or remove the x86 entry).
**Status** Active.

## D-012 — WinUI 3 app builds unpackaged via `dotnet build` (no VS, no workload)
**Date** 2026-09-02
**Decision** `PowerX.App` targets `net9.0-windows10.0.19041.0`, `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, `Platforms=x64;arm64`. It restores and builds green on this host with only the .NET 9 SDK — the WindowsAppSDK NuGet carries its own XAML compiler.
**Consequence** `PowerX.App` is **not** in `PowerX.sln` (the solution stays AnyCPU for Core/CLI/Tests). CI builds it as a separate `-p:Platform=x64` step. INPC rows are hand-rolled (the CommunityToolkit source-gen for partial properties didn't emit under this SDK; plain `[ObservableProperty]` fields warn MVVMTK0045 for WinRT marshalling, so we dropped the dependency).
**Status** Active. App builds with 0 warnings. Runtime visual QA still pending a desktop session.

## D-011 — CLI ships first, WinUI shell scaffolded alongside
**Date** 2026-09-02
**Decision** Milestone 1 delivers a working `PowerX.Core` + `powerx` CLI on real Windows telemetry, with the WinUI 3 app added as a scaffold that consumes the same Core services.
**Why** The build host has the .NET 9 SDK but no Visual Studio and no interactive desktop session, so a GUI cannot be visually QA'd here. The Core/CLI slice is fully buildable, testable and runnable now; it also satisfies the prompt's rule that GUI and CLI call the same underlying functionality. GUI polish happens on a machine where it can be seen.
**Status** Active. Core + CLI build green, 11 tests pass, `powerx status/process/tweak/scan/history` verified against live data.

## D-010 — Tweak state model: Default / Applied / Custom / NotApplicable / Unknown
**Decision** Detection compares the live registry value against both the shipped Windows default and our desired value. Anything else is `Custom` (do not silently overwrite).
**Why** Users and other tools also change these keys. Reporting `Custom` honestly and requiring an explicit apply is safer than assuming.
**Status** Implemented in `RegistryTweakOperation`.

## D-009 — Every mutation goes through `TweakEngine.Execute` and is logged
**Decision** No registry writes in UI/CLI handlers. `Execute` does detect → privilege check → build check → apply → verify → append `ChangeRecord` to `%LOCALAPPDATA%\PowerX\change-history.jsonl`.
**Why** Enables undo, history timeline, CLI parity, dry-run, and testability. Single audited path.
**Status** Implemented.

## D-008 — Providers return `ProviderResult<T>` with an explicit quality signal
**Decision** `Reliable` / `Approximate` / `Unavailable`. Consumers must render "unavailable", never fabricate `0`.
**Why** Prompt §58/§10 — no fake `0°C`, no fake precision.
**Status** Implemented for CPU and memory.

## D-007 — Process enumeration via a single `NtQuerySystemInformation(SystemProcessInformation)`
**Decision** One syscall per refresh, delta-based CPU% and I/O rate against the previous snapshot, rather than per-process Win32 handles.
**Why** This is how Task-Manager-class tools stay cheap under process storms (prompt §38). Opening thousands of handles per second does not scale.
**Trade-off** Struct layout is community-documented, not fully in `winternl.h`. Guarded by tests that assert the current process is found and all values are in range. Per-process image path / signature / user need a follow-up handle and are resolved lazily.
**Status** Implemented; 4 telemetry tests green.

## D-006 — CPU%: `GetSystemTimes` for total, `NtQuerySystemInformation(ProcessorPerformance)` for per-core
**Why** Documented, cheap, no PDH/WMI dependency or counter-corruption risk. PDH stays available as a later provider for counters these APIs don't expose.
**Status** Implemented.

## D-005 — Central package management + `Directory.Build.props`; pin SDK via `global.json`
**Status** Done. SDK 9.0.200.

## D-004 — Language/runtime: C# on .NET (now .NET 10 — see D-013)
**Why** `net10.0-windows` gives full Win32/registry access and source-generated P/Invoke (`LibraryImport`) with zero extra packages. No native C++/Rust component until profiling justifies one (prompt §35).
**Status** Superseded by D-013.

## D-003 — Hand-written `LibraryImport` P/Invoke, not CsWin32 (for now)
**Why** Keeps the interop surface tiny, readable and reviewable (prompt §64 "no unexplained constants"). CsWin32 can be introduced if the surface grows large.
**Status** Active. See `src/PowerX.Core/Interop/`.

## D-002 — License: MIT for our code (pending final confirmation), with a hard rule against importing GPL/restricted code
**Why** Maximises reuse and contribution. Winhance's no-compete clause and GPL projects mean we study behaviour/UX only and reimplement from Microsoft docs. See `docs/research/LICENSE_REVIEW.md`.
**Status** Proposed — needs sign-off before first tagged release.

## D-001 — Product framing: "Windows Control Center", not "debloater"
**Why** Prompt §7. Monitor / Diagnose / Optimize / Configure / Clean / Repair / Manage / Understand. The tool must be useful to someone who never debloats.
**Status** Active — drives the information architecture in `docs/PRODUCT_SPEC.md`.
