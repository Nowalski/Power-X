# Codebase audit — performance, blockers, correctness

Snapshot after the milestone-1 feature push (Repair, Programs, System Restore, update check, …).
Reviewed: telemetry hot path, page load paths, interop lifetime, streaming console, threading,
the tweak/cleanup/repair engines. `✅` = fixed in this pass, `▶` = open (with priority).

## Performance

| # | Finding | Status |
|---|---|---|
| P1 | **Tools page blocked the UI thread on navigate** — `powercfg /list` (process spawn) and a WMI `MSFT_PhysicalDisk` query (+ `GetRelated` per disk) ran synchronously in the constructor. | ✅ moved to `Task.Run` (`InitAsync`, `RefreshPowerAsync`, `BuildDisksAsync`) |
| P2 | **Network sampled every tick, app-wide** — `NetworkInterface.GetAllNetworkInterfaces()` + `GetIPProperties()` per NIC is the priciest part of a tick (~10–20 ms) and ran even when no page needed it. | ✅ `TelemetryHub` samples network every 2nd tick; history holds flat between |
| P3 | `ProcessProvider.Enumerate()` re-`AllocHGlobal`'d its `NtQuerySystemInformation` buffer (~1 MB) every tick. | ✅ buffer is now a field, allocated once and grown only when the syscall reports `InfoLengthMismatch`; freed in a finalizer |
| P4 | `AreaChart.Redraw()` rebuilds a ~360-segment bezier `PathGeometry` on every `SetData` (1/s per chart, up to ~5 on Home). ~0.5 ms each. | ▶ low — resize is debounced (50 ms coalesce); measured ~0.4 ms/chart, no visible cost. Left as-is |
| P5 | Pages were re-created on every navigation (`NavigationCacheMode.Disabled`) — Debloat/Programs/Services/Startup re-ran their async scan each visit. | ✅ `NavigationCacheMode="Enabled"` on those four; Rescan buttons stay for freshness |
| P6 | `HomePage` / `SettingsPage` constructors call `SystemInfoProvider.Collect()` synchronously. | ▶ low — registry + `GlobalMemoryStatusEx` only, < 2 ms |

## Correctness / blockers

| # | Finding | Status |
|---|---|---|
| C1 | **Repair "Driver list" crashed the app** — `driverquery /v` streamed thousands of wide lines; each line did `DispatcherQueue.TryEnqueue` + rewrote the whole `TextBox`, flooding the dispatcher and overrunning the control. | ✅ output is buffered in a `ConcurrentQueue`, flushed on a 120 ms timer, capped at 60 KB; `driverquery` drops `/v`; `RunSequence` wrapped in try/catch/finally |
| C2 | **Stray "Ctrl K" tooltip** appeared on hover anywhere — the root-Grid `KeyboardAccelerator` auto-shows its tooltip. | ✅ `KeyboardAcceleratorPlacementMode="Hidden"` on the grid |
| C3 | **`chkdsk C: /f /r /x` job would hang** — chkdsk prompts Y/N to schedule and we don't provide stdin. | ✅ now `cmd /c echo Y\| chkdsk …` |
| C4 | `MemoryTest` could induce paging on a low-RAM machine if other apps grab memory between `SafeMaxBytes()` and allocation. | ✅ re-checks `ullAvailPhys` every 8 chunks during allocation and stops early if it drops below 512 MB, recomputing the work total from what was actually allocated |
| C5 | `WindowsUpdateControl.Status()` only inspects `wuauserv` Start; on builds where `WaaSMedicSvc` is protected it may re-enable parts. | ▶ low — `Restore` puts everything back; make the status check consider more services |
| C6 | `ScheduledTasks` walks the tree via `dynamic` COM; a malformed task can throw mid-walk. | ✅ already caught per-task and per-folder |

## Resource / lifecycle

| # | Finding | Status |
|---|---|---|
| L1 | All `TelemetryHub.Subscribe` tokens are disposed in `OnNavigatedFrom`; `ProcessInspector` is disposed by the caller; `RepairPage` cancels + stops its flush timer on navigate-away. | ✅ verified |
| L2 | Every `Marshal.AllocHGlobal` / `OpenProcess` / `OpenSubKey` in Core is paired with a `finally` free/close or a `using`. | ✅ spot-checked all 17 interop-touching files |
| L3 | `GpuMetricsProvider` PDH query handle lives for the app lifetime and is only freed by `Dispose()` (never called — the hub holds it). | ▶ low — released by the OS on exit; fine for a desktop app |
| L4 | `ChangeLog` (`change-history.jsonl`) grew unbounded. | ✅ rotates to the last 4000 lines once the file passes 1 MB |
| L5 | Empty `catch` blocks (11, across cleanup / scheduled-tasks / env / net) are all deliberate "skip inaccessible / best-effort cleanup" and commented. | ✅ acceptable per `QA_PLAN.md` |

## Safety model (spot-check — this is the product's core promise)

- Every destructive action (process kill, service disable, app removal, cleanup, WU disable, firewall reset) shows a confirmation with the consequence stated. ✅
- Tweaks are reversible and recorded; the tweak engine verifies after writing. ✅
- WU "Disable completely" is a labelled security trade-off with a prominent Restore. ✅
- Cleanup is size-first and per-category; no Prefetch deletion, no standby-list purge. ✅
- No `irm | iex`, no bundled binaries, no security exclusions, no piracy/activation. ✅
- The update check never downloads or executes anything (D-017). ✅

## Follow-ups worth doing

1. **C5** — widen `WindowsUpdateControl.Status()` to consider `wuauserv` + `WaaSMedicSvc` + `UsoSvc` + policy, not just `wuauserv` Start.
2. **L3** — call `GpuMetricsProvider.Dispose()` from a `TelemetryHub` shutdown hook (currently OS-reclaimed on exit).
3. Wire the idle-budget benchmark job from `BENCHMARK_PLAN.md` into CI as a non-gating report.

---

## Second pass — after profiles / theme / debloat push (milestone-1-foundation)

Reviewed: `OptimizationProfile` + `Profiles`, the Tweaks-page profile strip, `ProfileCommand`,
theme/backdrop/accent plumbing, the expanded `DebloatCatalog`, the Tools shortcuts + Learn card.

| # | Finding | Status |
|---|---|---|
| A1 | `TweaksPage.ProfileApply_Click` called `TweakEngine.GetAllStatus()` up to 3× (each = detect-all). | ✅ single call, results reused; non-restore path shares one `TweakContext` across `GetStatus` |
| A2 | Profile apply + restore-point creation ran on the UI thread. | ✅ both wrapped in `Task.Run`; UI updates marshalled back after |
| A3 | `SystemBackdrop = null` ("None") left the NavigationView content area unpainted on some builds. | ✅ `ApplyBackdrop` sets an opaque `ApplicationPageBackgroundThemeBrush` on the root panel only when solid, transparent otherwise |
| A4 | Custom accent could desync from Windows if only `SystemAccentColor` were overridden. | ✅ all six `SystemAccentColor{Light,Dark}{1,2,3}` tints derived by lerp toward white/black; "System" leaves them untouched; applied once at launch before first brush resolves |
| A5 | Debloat `Match()` is a first-substring-wins scan — a new short key could shadow a longer one. | ✅ regression test: keys unique (case-insensitive) and no key is a substring of another entry |
| A6 | Many Tools shortcut buttons overflow a narrow window. | ✅ each shortcut row is its own horizontal `ScrollViewer` (vertical scroll disabled) |
| A7 | Profiles only ever reference safe tweaks. | ✅ test asserts every profile tweak id resolves and is not `SecurityTradeoff` / `Destructive`; `lowspec`/`gaming` exclude anything that weakens a boundary |

No new blockers. `dotnet test` = 21 passing. App builds clean (x64, warnings only — all pre-existing analyzer suggestions).

---

## Third pass — layout / responsiveness / Processes

User-reported issues, all addressed:

| # | Finding | Status |
|---|---|---|
| B1 | CPU / Memory / GPU / Network pages capped at ~1000–1120 px and left-aligned, so a wide window showed a big empty band on the right. | ✅ raised caps to 1500–1600 px and added responsive two-column card grids (`AdaptiveTrigger` at 1000 px) that collapse to one column when narrow |
| B2 | Tweaks profile strip was a horizontal `ScrollViewer` — at narrow widths the off-screen cards were unreachable ("profiles disappear"). Card Apply buttons didn't line up (different description lengths). | ✅ new `WrapPanel` control; profiles wrap into a uniform-size card grid inside the page's own vertical scroll; description clamps to 5 lines so every Apply button sits on a shared baseline |
| B3 | Repair page: memory-test expander sat flush against the window's bottom edge. | ✅ `Margin` + inner padding on the expander, `MinHeight` on the console row so it yields space |
| B4 | `NetTools.DnsLookupAsync` surfaced a raw `SocketException` for an IP with no PTR record — read as "DNS is broken". | ✅ rewritten: IP → reverse lookup with a plain "no PTR record" line; name → A/AAAA list then reverse-resolve each; typed `SocketError` handling for NXDOMAIN |
| B5 | Processes columns were fixed width — no way to widen Name or the numeric columns. | ✅ drag-resizable columns via a shared `ProcessColumns` model bound by both header and rows, `ColumnGrip` strips on each boundary, widths persisted to settings, "Reset columns" button |

Watch items for runtime QA: `{Binding}` on `ColumnDefinition.Width` inside the Processes `DataTemplate` (supported in WinUI 3, unlike UWP) — verify header and rows stay aligned while dragging. `AdaptiveTrigger` breakpoints use *window* width, which includes the 220 px nav pane.

---

## Fourth pass — column-resize regression, first-paint, security tweaks

| # | Finding | Status |
|---|---|---|
| C1 | Processes column drag did nothing — `{Binding}` on `ColumnDefinition.Width` does **not** propagate in WinUI 3 after all (works in the header once, never in virtualized rows). | ✅ widths applied imperatively: `ProcessColumns` (INPC) → header grid + every realized row grid via `ContainerContentChanging` and on `PropertyChanged`. Verified drag moves both. |
| C2 | Metric-page content was briefly centered — a centered `StackPanel` in WinUI collapses to its *content* width, making cards narrow on wide screens. | ✅ back to stretch + `MaxWidth` 1600; the 2-column detail grids consume the width so the residual right margin is small on typical displays. |
| C3 | Indexed VisualState setter `DetailGrid.(Grid.ColumnDefinitions)[1].Width` is fragile. | ✅ named the second `ColumnDefinition` and set `SecondCol.Width` directly; named + captioned visual states. |
| C4 | CPU / Memory / GPU spec & module rows blew up vertically at small widths (fixed 150 px label + wrapping value in a cramped column). | ✅ `Auto` label + `MinWidth`-guarded wrapping value; installed-modules rows stack label-over-value. |
| C5 | Memory hero clipped "12.3 GB / 32.0 GB". | ✅ hero is now a `RingGauge` (% in use); the GB figures moved to the two-line sub-caption. CPU & GPU heroes use the gauge too, matching Home. |
| C6 | First few seconds after launch showed empty charts crawling in. | ✅ `MetricRing.Seed()` pre-fills the ring with the first sample; `TelemetryHub.Start()` posts the first full sample at `Low` dispatcher priority instead of running it inline in the `MainWindow` constructor. |
| C7 | Tools shortcut rows had a horizontal scrollbar crowding the buttons. | ✅ they use `WrapPanel` now — buttons wrap, no scrollbar. |
| C8 | No way to turn off SmartScreen / firewall / Defender / UAC even when the user wants to. | ✅ new **Security (advanced)** tweak category — `SecurityTradeoff` risk, never `Recommended`, never in a profile, explicit confirmation, fully reversible. Defender entry documents the Tamper-Protection caveat; UAC entry documents that it breaks packaged apps. |

---

## Fifth pass — micro-stutter, column drag (again)

| # | Finding | Status |
|---|---|---|
| D1 | Column drag oscillated / went the wrong way ("opposite axis, very very buggy"), grabbing near CPU hit PID's edge. Two rewrites of the grip-per-edge approach didn't fix it: a grip on a column's right edge that resizes *that* column doesn't move when the flexible Name column absorbs the change, so the line never tracked the cursor. | ✅ **Third rewrite, different model.** No grip control. Pointer events handled on `HeaderGrid` (a `ResizeGrid : Grid`) with `handledEventsToo`. Five boundaries, each transfers width between its two adjacent columns (Excel-style; Name flexes for the first). The boundary tracks the cursor exactly; delta clamped so both neighbours stay in range; a thin accent guide line shows the boundary on hover and drag. Coordinates always measured against the never-moving `HeaderGrid`. |
| D2 | **Telemetry sampled on the UI thread** every second — `NtQuerySystemInformation` + PDH GPU read + (every other tick) `GetAllNetworkInterfaces`, ~5–15 ms on the render path = periodic micro-stutter. | ✅ `TelemetryHub` runs a background loop: the costly Win32/PDH/WMI calls happen off-thread, only the commit (store `Last*`, push history rings, raise `Updated`) is marshalled to the UI thread via `DispatcherQueue.TryEnqueue`. Providers stay single-threaded (one loop). Hidden-window backoff kept; alt-tab back resumes fast sampling within 1 s. |
| D3 | `AreaChart.Redraw()` allocated a fresh `PathGeometry` + ~300 `BezierSegment`s per figure **every second** per visible chart. | ✅ geometry (line, fill, gridlines) is built once in the ctor; a redraw mutates `StartPoint` and each segment's points in place. Sample count is stable at 300 once seeded, so steady-state redraw allocates nothing. |
| D4 | `NetworkPage` `CancellationTokenSource` cancelled but not disposed. | ✅ disposed in `RunDiag`'s `finally`; `OperationCanceledException` swallowed. |

### Remaining follow-ups
- `RingGauge.Render()` still rebuilds two small arc geometries per animation frame — 1 instance on the visible page, trivial Gen0 churn, left as-is.

---

## Sixth pass — Processes heat, chart clip, Freeze, Network list

| # | Finding | Status |
|---|---|---|
| E1 | Process-row heat washes were effectively invisible (12 % threshold, alpha capped at 0x66 on an ~90 px cell). | ✅ washes deepen smoothly from 3 % (alpha ~34 → ~210); new blue→amber→red **left accent bar** per row for the CPU hogs; CPU %/working-set numbers go SemiBold when the process is notable. |
| E2 | `AreaChart` looked "cut off" at some window sizes — the clip rect was only updated on the 50 ms resize debounce, never on `SetData`, so a redraw landing between a resize and the timer used the old (smaller) bounds. | ✅ clip is a reused `RectangleGeometry` set on every `Redraw`; `Redraw` runs immediately on `SizeChanged` (alloc-free now); latest-sample dot clamped inside the bounds. |
| E3 | Network "Active connections" was a bare `ItemsControl` with `MaxHeight` — rows past 360 px were silently clipped with no scrollbar. | ✅ wrapped in a `ScrollViewer`; hero charts moved off a nested `*/*` Grid onto a `StackPanel`; connection columns got MinWidths + spacing. |
| E4 | **Sorting (and filtering) did nothing while Freeze was on** — `OnTick`'s freeze guard blocked every caller, including the explicit sort/filter path. | ✅ the guard only blocks automatic telemetry ticks now; a frozen sort reorders the on-screen rows in place (keeping their frozen values); unfreezing refreshes immediately. |

---

## Seventh pass — layout-fill unification, GPU VRAM, memory slots, fresh sweep

| # | Finding | Status |
|---|---|---|
| F1 | **The recurring "content cut off at large window widths, fine when smaller" bug.** A `Stretch` element with `MaxWidth` *smaller* than the available width does not fill to its `MaxWidth` in WinUI 3 — it collapses to its content's desired width and drifts right. Every metric/tool page hit this at wide sizes. `ScrollViewer.HorizontalContentAlignment="Stretch"` alone was not enough. | ✅ `PageLayout.CenterCap(page, content, cap)` — centres the content and sets an explicit `content.Width = min(cap, viewport − gutter)` off `page.SizeChanged`. Applied to every page (caps 900–1500). Verified by screenshot at 2400 px: nothing clipped, Network diagnostics row + all interface cards render fully. |
| F2 | **GPU VRAM reported "4 GB" for a 16 GB card.** `Win32_VideoController.AdapterRAM` is a signed 32-bit field and saturates at 4 GiB. | ✅ fall back to the driver's registry key `HardwareInformation.qwMemorySize` (64-bit QWORD / 8-byte binary) when it exceeds the WMI value, matched to the adapter by `DriverDesc`. Verified: RTX 5080 now shows 15.9 GB on the GPU page and Home. |
| F3 | **Two RAM sticks both labelled "DIMM 1"** — some boards return the same `DeviceLocator` for every module. | ✅ `MemoryHardware.Query` disambiguates: use `BankLabel` when it is unique across modules, otherwise number the slots. Verified: now "P0 CHANNEL A / P0 CHANNEL B". |
| F4 | Network hero wasted a wide fixed gutter column on a two-line `↓ / ↑` value block, leaving a large gap before the charts; the diagnostics output box was over-tall when empty. | ✅ hero is now a per-row `label + chart` grid (`Download` / `Upload` captions, arrow dropped from the value); diagnostics box trimmed to 148 px. |
| — | Fresh sweep of `TelemetryHub` loop, `NetworkPage` diagnostics/connection paths, `ConnectionProvider`, and all `[0]` / `.First()` / `.Result` call sites. | No new blockers. All indexer access is length-guarded or on non-empty GroupBy results; `.GetAwaiter().GetResult()` is confined to CLI sync entry points. `dotnet build` clean (analyzer suggestions only). |

---

## Eighth pass — process/tweak lifecycle, spawn safety, provider honesty, portable build

| # | Finding | Status |
|---|---|---|
| G1 | **`ProcessDetailsProvider` integrity level was a dead field** — `QueryToken` returned `""` on every path; the `TokenIntegrityLevel` constant was unused. | ✅ parses the token's mandatory-label SID (Untrusted/Low/Medium/High/System); the inspector shows it; overview load moved off the UI thread (`Task.Run`). |
| G2 | **Efficiency mode OFF flattened priority to Normal** — a user's High/AboveNormal choice was silently lost. | ✅ the pre-eco priority class is recorded when eco is turned on and restored when turned off. |
| G3 | `OpenLocation` / `CopyPath` ran the full `FileVersionInfo` + token `Resolve()` on the UI thread just for a path. | ✅ new lightweight `ProcessDetailsProvider.ImagePath()` (single query, no disk read). |
| G4 | **`RegistryTweakOperation.ReadValue` swallowed `SecurityException` / `UnauthorizedAccessException` → returned null → Detect reported "Default".** An unreadable (needs-elevation) key was indistinguishable from an absent value. | ✅ those two now throw with context; `TweakEngine.GetStatus` surfaces `Unknown` + reason; `TweakEngine.Execute`'s previously-unguarded pre-`Detect` is wrapped. Regression tests: absent → Default, throwing Detect → Unknown and Execute survives. |
| G5 | **Process-spawn helpers could deadlock / misreport.** `PowerPlans.Run` read stdout then stderr sequentially and read `ExitCode` after a bare `WaitForExit(timeout)`; `QuickActions.RunHidden` redirected pipes it never drained. | ✅ new `ProcessRunner.Run` — async pipe draining, hard timeout, process-tree kill on timeout, `ExitCode` never read before exit. Both callers routed through it. |
| G6 | `InstalledPrograms.SplitCommand` split an **unquoted** `UninstallString` at the first space — `C:\Program Files\App\uninstall.exe /S` → file `C:\Program`. | ✅ splits at the first executable-extension boundary; made public + 5 test cases. |
| G7 | **Windows Update `Disable()`/`Restore()` did not preserve prior state** — service Start types and AU/WU policy values were overwritten with no record; `Restore()` hardcoded "defaults", re-enabling a service the user had deliberately disabled and deleting an org policy. | ✅ `Disable()` snapshots service Start values, present policy values and task enabled-states to `HKLM\SOFTWARE\PowerX` before changing anything; `Restore()` replays the snapshot exactly (fallback to documented defaults only when no snapshot). `ScheduledTasks.GetEnabled` added. |
| G8 | **`SystemRestore.Create()` set `SystemRestorePointCreationFrequency=0` and never restored it** — every trigger created a restore point forever after. | ✅ prior value captured and restored (or deleted if absent) in a `finally`. |
| G9 | **`ConnectionProvider` never listed IPv6 UDP** (no `ReadUdp6`) and returned nothing if the endpoint table grew between the size-probe and the read. | ✅ `ReadUdp6` + `MIB_UDP6ROW_OWNER_PID`; shared `ReadTable` retries on `ERROR_INSUFFICIENT_BUFFER` (≤4 attempts). |
| G10 | `NetworkPage.RefreshConnectionsAsync` was fire-and-forget with no re-entrancy guard — a slow scan could stack up and a stale result clobber a fresher one. | ✅ single-flighted; drops its result if the page was navigated away from. |
| G11 | `ChangeLog.RevertableChanges()` listed any tweak whose latest successful record was an `Apply`, even if it ended up Custom/Default or a later Apply was a no-op after a failed Revert. | ✅ requires `ResultingState == Applied`; test with 5 scenarios. |
| G12 | **`StartupProvider.SetEnabled` on a RunOnce entry was a silent no-op** — it wrote a `StartupApproved` marker, which Windows ignores for RunOnce; PowerX reported success and showed the entry "off" while it still ran. | ✅ refuses with an honest message; `CanToggle()` exposed; the Startup page disables the toggle for RunOnce rows with an explanatory line. |
| G13 | **`ScheduledTasks.GetEnabled`/`SetEnabled` leaked every COM object** (service + folder + task) per call — and `WindowsUpdateControl` calls them 8× in a loop. | ✅ `WithTask` helper with `try`/`finally` + `Marshal.FinalReleaseComObject` on every COM object; `Walk()` releases per-task; new `SetEnabledMany` batches over one connection. |
| G14 | **`Services` / `Programs` / `Startup` pages showed a blank page** (no `try`/`catch`) when their enumeration threw; `ToolsPage.InitAsync` ran 6 load steps unguarded so one failure skipped the rest. | ✅ each load catches, logs, and writes the reason into the page summary; Tools steps are individually guarded. |
| G15 | **The self-contained portable publish did not include the Visual C++ runtime** that native `Microsoft.UI.Xaml.dll` links against — on a clean PC the app died during MainWindow XAML load ("nothing happens after UAC"). | ✅ `IncludeVcRuntime` publish target bundles `vcruntime140*`/`msvcp140*`/`concrt140` app-local; `App.OnUnhandledException` now also logs the XAML-parse detail from `UnhandledExceptionEventArgs.Message`. |
| — | CLI: `history` printed pre-formatted markup literally (`[green]✓[/]`) because the icon was interpolated into `MarkupLineInterpolated`. | ✅ rewritten with `MarkupLine` + `Markup.Escape` on user fields; ASCII status glyphs; `Console.OutputEncoding = UTF-8` set at startup. |

Tests: 30 pass. Core + CLI + app build clean (x64). Runtime-checked: all metric/tool pages rendered via screenshot at multiple window sizes; portable folder build launches and renders end to end.

---

## Ninth pass — new-code audit (crash diagnostics) + follow-ups

New surface reviewed: `PowerX.Core/Diagnostics/Crash/*` (WerReportReader, EventLogCrashReader,
BugcheckCatalog, MinidumpReader, CrashScanner), `CrashCommand`, `CrashPage`.

| # | Finding | Status |
|---|---|---|
| H1 | `MinidumpReader` parses attacker-influenceable files (a crafted `.mdmp` in a WER folder). | ✅ every RVA + size checked against file length; stream/module counts capped (128 / 4096); stream body capped at 8 MB; strings capped at 64 KB; no memory streams read, no pointer-following; malformed → `Unreadable`, never a throw. Test: bad signature, RVA past EOF, and a hand-crafted valid dump. |
| H2 | `EventLogCrashReader` — `EventLogReader` / `EventRecord` are `IDisposable`; `FormatDescription()` and `Properties` can throw. | ✅ `using` on every record, `reader.Dispose()` in `finally`, per-record `try`/`catch`, `ReverseDirection` + `max` cap so the whole log is never walked. |
| H3 | `CrashPage.ScanAsync` was fire-and-forget from the ctor and from every combo/checkbox change — concurrent scans could interleave `List.Children` rebuilds. | ✅ `_scanGen` generation counter; a superseded scan discards its results. |
| H4 | CLI `crashes show` / detail view interpolated pre-formatted markup (same class as the `history` bug). | ✅ `MarkupLine` + `Markup.Escape` on every dynamic field. |
| — | Sweep for `TODO` / `FIXME` / `NotImplemented` / mock data / `.Result` / `.Wait()` / `Thread.Sleep` on the UI thread / registry writes outside their feature module. | None found. All `_ = LoadAsync()` page-load calls now carry an error state (8th pass). Registry writes live in dedicated, individually-reversible feature modules (SystemRestore, WindowsUpdateControl, ServiceProvider, StartupProvider) — the "route through TweakEngine" rule is for tweaks. |

Tests: 42 pass. Solution + app build clean (x64).

---

## Tenth pass — installer / updater surface + data-at-rest integrity

| # | Finding | Status |
|---|---|---|
| I1 | **`AppSettings.Save` used a truncating `File.WriteAllText`** — a crash (or the MSI installer's Restart Manager killing the app) mid-write left `settings.json` corrupt, and `Load` silently fell back to defaults → every preference lost. | ✅ writes a temp file and `File.Replace`s it in, keeping the prior good copy as `.bak` which `Load` falls back to; `JsonSerializerOptions` cached. |
| I2 | **`ChangeLog.RotateIfLarge`** did the same truncate-write, and trimmed to 4000 lines at a 1 MB trigger — with ~250-byte records that re-rotated on almost every append near the limit. | ✅ 2 MB trigger / keep 2000, temp-file swap. +2 tests (rotation stays bounded, never loses the file). |
| I3 | Updater `UpdateInstaller.DownloadVerifiedAsync` — a "download and run" path for an elevated tool. | ✅ only an `https` `github.com` / `objects.githubusercontent.com` URL is accepted, the size **and** SHA-256 are verified against the manifest before anything runs, a mismatch is refused ("It was NOT run"), a prior verified download is reused, and stale installers for other versions are pruned. +6 tests. |
| I4 | Disk cleanup could not be stopped once started. | ✅ `CleanupScanner.Clean` takes a `CancellationToken`; the Tools "Clean selected" button becomes "Stop" during the run. |
| — | Installer: verified by decompiling the built MSI that there is exactly one `<Shortcut>`, in the Start-menu `PowerX` folder — **no `DesktopFolder`**, no desktop shortcut. | ✅ `installer/PowerX.wxs`. |
| — | WMI `ManagementObject` / `ManagementBaseObject` enumerations (StorageInfo, GpuMetricsProvider, MemoryHardware, SystemRestore) don't `Dispose` each item — finalizer-reclaimed, not a hot path. | ▶ noted; a CA2000 sweep, low priority. |

Tests: 51 pass. Solution + app + CLI + MSI build clean (x64).

---

## Eleventh pass — updater OS-compatibility gate

| # | Finding | Status |
|---|---|---|
| J1 | **`version.json` carries `minimumWindowsBuild` but `UpdateChecker` never read it.** A release that needs a newer Windows build than the running PC would still surface the "Download & install" button and hand the user a hash-pinned MSI that cannot run on their OS. | ✅ `UpdateChecker.Build` now compares `minimumWindowsBuild` against `Environment.OSVersion.Version.Build`. When the OS is too old the update is still announced, but the installer fields are dropped (so `HasVerifiedInstaller` is false → "Open releases" only) and the notes explain which build is required and which this PC is on. +2 tests. |
| — | `UpdateInstaller` follows HTTP redirects to any host. | ▶ acceptable: content is SHA-256 + size pinned to the manifest, so a redirect cannot substitute a payload; the host allow-list is on the manifest URL. Noted, not changed. |
| — | `UpdateChecker.ManifestUrl` and the Settings/About links still point at the **private** `Nowalski/PowerX`. | ✅ retargeted to the public `Nowalski/Power-X` (manifest URL, "Open releases" fallback, About links, `version.json` url). |

Tests: 53 pass. Solution build clean.
