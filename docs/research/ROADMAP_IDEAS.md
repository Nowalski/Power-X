# Roadmap ideas, what else to add

Researched candidate features, scored against PowerX's principles (truthful telemetry,
evidence-backed tweaks, safety model, don't-be-the-hog, premium native feel). Each entry:
value, effort, risk, prior art, verdict. Newest research pass: 2026-09-02.

Legend: **effort** S/M/L, **risk** = how easily it violates a principle or destabilises Windows.

---

## Tier 1, high value, fits the model, do next

### 1. Startup / autostart impact  [PARTLY SHIPPED 0.1.4]
- **Value** High. Startup delay is the single most defensible "make my PC faster" win.
- **Shipped in 0.1.4**: `BootPerformance` reads `Microsoft-Windows-Diagnostics-Performance/Operational`
 (events 100/101/102/103, the same source Task Manager's "Startup impact" uses). The Startup page
 now shows a boot-time card ("Last boot took 41.6 s, 6.7 s slower than your recent average") and a
 "High / Medium / Low impact, +Xs at boot" tag on any entry Windows measured as slow.
- **Still to do**: fuller location coverage. Scheduled-task logon triggers already surface through
 the Scheduled Tasks list, but `HKLM...\Run`, Winlogon `Userinit`/`Shell` and shell extensions do not.
- **Effort** S remaining, **Risk** low (read-only), **Prior art** Autoruns, Task Manager.

### 2. Network activity page, per-process connections + rate  [SHIPPED 0.1.1]
- **Value** High. "What is this program talking to?" is a top support question.
- Current: `NetTools` + `GetExtendedTcpTable` exist. Need a live page: per-process rows,
 remote address, state, up/down rate, and **opt-in** reverse-DNS / WHOIS (never automatic).
- **Effort** M, **Risk** low (remote lookups gated), **Prior art** TCPView, System Informer.
- **Verdict** Shipped in 0.1.1: live rate charts, per-process connections, a listening-ports view with a network-reachable flag, a connection-state summary, opt-in reverse DNS, and copy. Per-connection byte rate needs ETW and is a fast-follow.

### 3. Scheduled Tasks viewer/curator  [SHIPPED 0.1.7]
- **Value** Medium-high. Lots of OEM + telemetry tasks; users want them visible.
- Curate a **known-task catalog** (like the debloat catalog) with a stance per task; leave the
 long tail read-only with "disable" (reversible) and never "delete".
- **Effort** M, **Risk** medium, disabling the wrong task breaks features; mitigate with the
 same detect to show to confirm to log to undo flow and a KeepSystem list.
- **Verdict** Yes after #1.

### 4. System report export (support bundle)  [SHIPPED 0.1.1]
- **Value** High for the "Understand" pillar and for the project's own bug reports.
- One command produces a reviewed, PII-aware Markdown or JSON report: hardware, OS build, drivers, top
 processes, applied tweaks, recent change history, disk health, event-log error summary.
- **Effort** M, **Risk** low (must scrub serials / usernames by default, show a preview).
- **Verdict** Shipped in 0.1.1. `powerx report` (--print / --no-redact / --out) and a Settings button that previews the full text before saving. Redaction of the user name, machine name, MAC and serials is on by default. JSON output is a fast-follow.

---

## Tier 2, good, later

### 5. Driver inventory + update *check* (no auto-install)  [SHIPPED 0.1.7]
- 0.1.5's config snapshot records each `Win32_PnPSignedDriver` name and version, so the "What
 changed" page flags a driver version bump between snapshots. Still to do: a standalone driver
 list with age flags and a "check with the vendor" link.
- **Never** download or install a driver ourselves (same stance as D-017).
- **Effort** M remaining, **Risk** medium (must not become a "driver updater" scareware pattern).

### 6. Per-process drill-down parity with System Informer (user-mode only)
- Handles, modules, threads with start address, environment block, token groups. Capability-detect
 what needs a driver and stop cleanly, no kernel component (per).
- **Effort** L, **Risk** low, **Verdict** the "expert" milestone.

### 7. Memory drill-down (RAMMap-lite)
- Physical page categorisation (`SuperfetchInformation`), per-process working-set/private breakdown,
 standby list size. **Read-only**, no "empty standby list" button (folklore, harms perf).
- **Effort** L, **Risk** low if we resist the purge buttons.

### 8. Resource timeline
- A single scrubber correlating CPU/mem/disk/net/GPU with process events and our own change log,
 so "it got slow at 14:20" has an answer. Uses the existing `MetricRing` history, extended.
- **Effort** M-L, **Risk** low.

### 9. Winget-backed app management
- Show installed programs with winget IDs; offer update-all / selective update via winget
 (user-initiated, output streamed like the repair runner). Reuse `InstalledPrograms`.
- **Effort** M, **Risk** low-medium (winget must be present; handle its absence).

### 10. Config profiles: import / export / share  [SHIPPED 0.1.7]
- Export applied tweaks + selected debloat as a reviewable JSON; import shows a diff and
 confirmation before applying (never silent). Lets people share a setup without a script.
- **Effort** S-M, **Risk** low (import is just `ApplyMany` behind a preview), builds on D-018.

### 11. "Potato mode" hardware-aware auto-suggest
- On a low-RAM / HDD / low-core machine, *suggest* (not auto-apply) the lowspec profile + specific
 extras (e.g. disable search indexing on an HDD, not an SSD). Detection already partly in
 `CpuInfo` / `StorageInfo` / `MemoryHardware`.
- **Effort** S, **Risk** low.

---

## Tier 3, nice, low priority / needs care

- **Security page, SHIPPED 0.1.2.** Defender status (WMI), threat history, start a Defender scan,
  and a SHA-256 lookup against CIRCL hashlookup (free, open, no key). PowerX is not an antivirus:
  no signatures, no quarantine, no all-clear, no auto-removal. See D-025. Fast-follow: opt-in
  VirusTotal / MalwareBazaar keys for an engine-count verdict.
- **Context-menu / "Open with" editor**, popular in Winhance; medium risk (shell hive edits).
- **Storage treemap** (WizTree-style), the folder-size table shipped 0.1.5 as the Storage
 explorer page (`FolderSizer`, drill-down, `powerx storage`). A true treemap and an MFT-speed
 scan are the remaining upgrade.
- **Firewall rule viewer**, SHIPPED 0.1.7 read-only (`FirewallRules`, `HNetCfg.FwPolicy2`). Add/remove still not built.
- **Hosts-file manager with known blocklists**, must not ship opinionated blocklists; only
 edit + backup + toggle.
- **Battery report / power usage**, SHIPPED 0.1.5 (`BatteryHealth`: wear, cycles, runtime from
 `powercfg /batteryreport /xml`; Tools card + `powerx battery`). Fast-follow: SRUM per-app energy.
- **Pending-restart detector**, SHIPPED 0.1.5 (`PendingReboot`, Tools InfoBar, `powerx reboot`).
- **Component store (WinSxS) analysis**, SHIPPED 0.1.5 (`ComponentStore`, DISM analyze + safe cleanup).
- **Crash & dump insights**, a "Repair > Crash insights" timeline from WER `ReportArchive`,
 Application-log events 1000/1001/1002/1026, `Win32_ReliabilityRecords`, and (elevated,
 optional) `C:\Windows\Minidump` bugcheck codes. Full design + safety rules in
 [`CRASH_DIAGNOSTICS.md`](CRASH_DIAGNOSTICS.md). Never downloads symbols, never loads a dump
 into a debugger engine, never uploads. Reports always separate facts / likely causes /
 confidence / remediation / missing. Needs a `D-0xx` decision first.
- **RunOnce entry removal**, the Startup page can show RunOnce entries but not disable them
 (D-020); a reviewed "Remove" (delete the value, with the value stashed for undo) would close
 the loop.
- **Clipboard / env / services** quality-of-life editors, partly done in Tools.
- **Notification / focus assist scheduler**, thin value.
- **Theming: wallpaper-aware accent, custom hex picker**, after the preset accent lands.

## Explicitly NOT doing
- Registry "cleaners", one-click "boost", RAM "optimizers", standby-list purging, no evidence,
 potential harm. (docs/research/TWEAK_RESEARCH.md)
- Auto driver/software installation, bundled binaries, telemetry of our own.
- Disabling Defender / SmartScreen / UAC / firewall / VBS as first-class toggles
 (see COMPETITOR_MATRIX "GTweak" anti-pattern). Windows Update disable stays as the one
 clearly-labelled, prominently-reversible security trade-off.
- Anything touching activation / licensing.
