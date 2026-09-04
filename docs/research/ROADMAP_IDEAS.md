# Roadmap ideas — what else to add

Researched candidate features, scored against PowerX's principles (truthful telemetry,
evidence-backed tweaks, safety model, don't-be-the-hog, premium native feel). Each entry:
value → effort → risk → prior art → verdict. Newest research pass: 2026-09-02.

Legend: **effort** S/M/L, **risk** = how easily it violates a principle or destabilises Windows.

---

## Tier 1 — high value, fits the model, do next

### 1. Startup / autostart impact  [PARTLY SHIPPED 0.1.4]
- **Value** High. Startup delay is the single most defensible "make my PC faster" win.
- **Shipped in 0.1.4**: `BootPerformance` reads `Microsoft-Windows-Diagnostics-Performance/Operational`
  (events 100/101/102/103, the same source as Task Manager's Startup impact) and the Startup page
  now shows a boot-time card ("Last boot took 41.6 s, 6.7 s slower than your recent average") plus
  a "High / Medium / Low impact, +Xs at boot" chip on any entry Windows flagged as slow.
- **0.1.7**: an eligible Run entry can be delayed after sign-in (`StartupDelay`, a delayed-logon scheduled task).
- **Still to do**: fuller autostart coverage: `HKLM...\Run`, Winlogon `Userinit`/`Shell`, shell extensions.
- **Effort** S remaining · **Risk** low (read-only).

### 2. Network activity page — per-process connections + rate  [SHIPPED 0.1.1]
- **Value** High. "What is this program talking to?" is a top support question.
- Current: `NetTools` + `GetExtendedTcpTable` exist. Need a live page: per-process rows,
  remote address, state, up/down rate, and **opt-in** reverse-DNS / WHOIS (never automatic).
- **Effort** M · **Risk** low (remote lookups gated) · **Prior art** TCPView, System Informer.
- **Verdict** Shipped in 0.1.1: live rate charts, per-process connections, a listening-ports view (with a network-reachable flag), a connection-state summary, opt-in reverse DNS, and copy. Per-process byte rate shipped 0.1.7 (`NetworkUsageEtw`, a private ETW session via the TraceEvent package).

### 3. Scheduled Tasks viewer/curator  [SHIPPED 0.1.7]
- **Value** Medium-high. Lots of OEM + telemetry tasks; users want them visible.
- Curate a **known-task catalog** (like the debloat catalog) with a stance per task; leave the
  long tail read-only with "disable" (reversible) and never "delete".
- **Effort** M · **Risk** medium — disabling the wrong task breaks features; mitigate with the
  same detect→show→confirm→log→undo flow and a KeepSystem list.
- Partial today: logon/boot tasks show in the Startup page, and 0.1.5's "What changed" page
  flags a *new* scheduled task between snapshots. The dedicated curator page is still open.
- **Shipped 0.1.7**: `TaskInventory` + `ScheduledTaskCatalog` (~40 curated stances), Scheduled tasks page, `powerx tasks`. Toggling reuses the reversible `SetEnabled`.

### 4. System report export (support bundle)  [SHIPPED 0.1.1]
- **Value** High for the "Understand" pillar and for the project's own bug reports.
- One command → a signed-off, PII-aware Markdown/JSON: hardware, OS build, drivers, top
  processes, applied tweaks, recent change history, disk health, event-log error summary.
- **Effort** M · **Risk** low (must scrub serials / usernames by default, show a preview).
- **Verdict** Shipped in 0.1.1. `powerx report` (with `--print` / `--no-redact` / `--out`) and a Settings button that previews the full text before saving. Redaction of the user name, machine name, MAC and serials is on by default. JSON output is a fast-follow.

---

## Tier 2 — good, later

### 5. Driver inventory + update *check* (no auto-install)  [SHIPPED 0.1.7]
- **0.1.5**: `SystemSnapshot` records `Win32_PnPSignedDriver` name + version + provider, so the
  "What changed" page shows a driver version bump between two snapshots. Still to do: a
  standalone driver list with age flags and a "check with the vendor" link.
- **Effort** M remaining · **Risk** medium (must not become a "driver updater" scareware pattern).

### 6. Per-process drill-down parity with System Informer (user-mode only)
- Handles, modules, threads with start address, environment block, token groups. Capability-detect
  what needs a driver and stop cleanly — no kernel component (per prompt §35).
- **Effort** L · **Risk** low · **Verdict** the "expert" milestone.

### 7. Memory drill-down (RAMMap-lite)
- Physical page categorisation (`SuperfetchInformation`), per-process working-set/private breakdown,
  standby list size. **Read-only** — no "empty standby list" button (folklore, harms perf).
- **Effort** L · **Risk** low if we resist the purge buttons.

### 8. Resource timeline
- A single scrubber correlating CPU/mem/disk/net/GPU with process events and our own change log,
  so "it got slow at 14:20" has an answer. Uses the existing `MetricRing` history, extended.
- **Effort** M-L · **Risk** low.
- Note: the *config*-drift half of "why did it get slow" shipped as the **What changed** page
  in 0.1.5 (D-027). This item is the live-resource half.

### 9. Winget-backed app management
- Show installed programs with winget IDs; offer update-all / selective update via winget
  (user-initiated, output streamed like the repair runner). Reuse `InstalledPrograms`.
- **Effort** M · **Risk** low-medium (winget must be present; handle its absence).

### 10. Config profiles: import / export / share  [SHIPPED 0.1.7]
- Export applied tweaks + selected debloat as a reviewable JSON; import shows a diff and
  confirmation before applying (never silent). Lets people share a setup without a script.
- **Effort** S-M · **Risk** low (import is just `ApplyMany` behind a preview) · builds on D-018.

### 11. "Potato mode" hardware-aware auto-suggest
- On a low-RAM / HDD / low-core machine, *suggest* (not auto-apply) the lowspec profile + specific
  extras (e.g. disable search indexing on an HDD, not an SSD). Detection already partly in
  `CpuInfo` / `StorageInfo` / `MemoryHardware`.
- **Effort** S · **Risk** low.

---

## Tier 3 — nice, low priority / needs care

- **Security page — SHIPPED 0.1.2.** Defender status (WMI `MSFT_MpComputerStatus`), threat
  history (`MSFT_MpThreat` + `MSFT_MpThreatDetection`), start a Defender scan (`MpCmdRun -Scan`),
  and a SHA-256 lookup against CIRCL hashlookup (free, open, no key). PowerX is **not** an
  antivirus: no signatures, no quarantine, no all-clear, no auto-removal. See D-025.
  Fast-follow: opt-in VirusTotal / MalwareBazaar keys for an engine-count verdict; an
  Autoruns-style "worth reviewing" heuristic (scoped out for now — do not want the app making
  accusations).
- **Context-menu / "Open with" editor** — popular in Winhance; medium risk (shell hive edits).
- **Storage treemap** (WizTree-style) — **the folder-size table shipped 0.1.5** as the Storage
  explorer page (`FolderSizer`, drill-down, `powerx storage`). A true treemap visualisation and
  an MFT-speed scan are the remaining upgrade.
- **Firewall rule viewer** — SHIPPED 0.1.7, read-only (`FirewallRules`, `HNetCfg.FwPolicy2`). Add/remove is still deliberately not built.
- **Hosts-file manager with known blocklists** — must not ship opinionated blocklists; only
  edit + backup + toggle.
- **Battery report / power usage** — **SHIPPED 0.1.5** (`BatteryHealth`: wear %, cycles, runtime
  from `powercfg /batteryreport /xml` + `GetSystemPowerStatus`; Tools card + `powerx battery`).
  Fast-follow: SRUM per-app energy use (`/srumutil`), the slow `powercfg /energy` audit.
- **Pending-reboot detector** — **SHIPPED 0.1.5** (`PendingReboot`, Tools InfoBar, `powerx reboot`).
- **Component store (WinSxS) analysis** — **SHIPPED 0.1.5** (`ComponentStore`, DISM
  AnalyzeComponentStore + StartComponentCleanup, never ResetBase).
- **Event log browser** — SHIPPED 0.1.7 (`EventLogBrowser`, grouped + ~25 plain-language notes, `powerx events`).
- **Crash & dump insights** — a "Repair ▸ Crash insights" timeline from WER `ReportArchive`,
  Application-log events 1000/1001/1002/1026, `Win32_ReliabilityRecords`, and (elevated,
  optional) `C:\Windows\Minidump` bugcheck codes. Full design + safety rules in
  [`CRASH_DIAGNOSTICS.md`](CRASH_DIAGNOSTICS.md). Never downloads symbols, never loads a dump
  into a debugger engine, never uploads. Reports always separate facts / likely causes /
  confidence / remediation / missing. Needs a `D-0xx` decision first.
- **RunOnce entry removal** — SHIPPED (D-020, generalised further in D-029 0.1.10 to also cover
  a Run/RunMachine entry that names a specific program file which no longer exists).
- **A synthesis / "what's worth doing" view** — SHIPPED 0.1.10 as the **Health check** page
  (D-029): runs the checks PowerX already had against the whole machine and turns them into one
  prioritised list, each item pointing at the page that handles it. `powerx doctor`.
- **"Explain this process"** — SHIPPED 0.1.10 (D-029): a curated note for common processes plus a
  signed-and-in-System32 heuristic, shown in the Process inspector.
- **CSV export** on the list-heavy pages — SHIPPED 0.1.10 (Drivers, Firewall, Scheduled tasks,
  Event log).
- **Clipboard / env / services** quality-of-life editors — partly done in Tools.
- **Notification / focus assist scheduler** — thin value.
- **Theming: wallpaper-aware accent, custom hex picker** — after the preset accent lands.

## Explicitly NOT doing
- Registry "cleaners", one-click "boost", RAM "optimizers", standby-list purging — no evidence,
  potential harm. (docs/research/TWEAK_RESEARCH.md)
- Auto driver/software installation, bundled binaries, telemetry of our own.
- Disabling Defender / SmartScreen / UAC / firewall / VBS as first-class toggles
  (see COMPETITOR_MATRIX "GTweak" anti-pattern). Windows Update disable stays as the one
  clearly-labelled, prominently-reversible security trade-off.
- Anything touching activation / licensing.
