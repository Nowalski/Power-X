# Decision log

Running record of significant choices. Newest first. Each entry: context, decision, why, status.

---

## D-023. Distribution: a WiX MSI installer is the primary channel; portable exe secondary
**Date** 2026-09-03
**Decision** The main way to get PowerX is **`PowerX-Setup-<ver>-win-x64.msi`** (WiX 5, `installer/PowerX.wxs`, built by `installer/build.ps1`). One 53 MB file: per-machine install to `Program Files\PowerX`, a Start-menu shortcut and a desktop shortcut, Add/Remove Programs entry, in-place major upgrade (shared `UpgradeCode`), clean uninstall. The WinUI app stays **unpackaged and unchanged**, the MSI only lays down the self-contained publish folder (with the bundled VC++ runtime). WiX 5 (not 7, v7 needs the paid OSMF EULA) as a `dotnet tool`, build-time only. A single-file self-contained `PowerX.exe` remains available as a no-install portable option but is not the recommended one (slow first launch, AV false positives).
**Why** The 500-file self-contained folder is the correct WinUI 3 unpackaged layout but reads as junk to users. MSIX would mean re-architecting storage/`ms-appx`/elevation. An MSI is one file, standard, reproducible in CI, and needs no app changes.
**Status** Active. Not code-signed yet, so SmartScreen "unknown publisher". Elevated end-to-end install verified only by payload inspection + running the app from the extracted layout (the CI/dev shell can't elevate).

## D-022. Crash insights: read what Windows recorded, never analyse dumps with a debugger
**Date** 2026-09-03
**Decision** `PowerX.Core/Diagnostics/Crash` reads the WER `Report.wer` store, the Application + System event logs (1000/1001/1002/1026, WER-SystemErrorReporting 1001, EventLog 6008), `Win32_ReliabilityRecords` (deferred), and, only when the user ticks the box, only elevated, the *metadata* streams of a user-mode minidump (`MinidumpReader`: header + directory + SystemInfo/ModuleList/Exception, every RVA bounds-checked, no memory streams, no pointer-following). It does **not** download debugging symbols, load a dump into `dbgeng`/`dbghelp`, or upload anything. `CrashScanner` produces `CrashInsight` records that keep *observed facts* separate from *likely causes*, tag a `CrashConfidence` (Insufficient/Low/Moderate/High), and list *what is still missing*. It says "insufficient evidence" rather than guessing. `BugcheckCatalog` is ~24 hand-curated stop codes. Kernel dumps are never parsed (needs a debugger engine + symbols). New first-party dependency `System.Diagnostics.EventLog`. Surfaces: `powerx crashes` and a "Crash insights" page under Optimize & fix.
**Why** The evidence Windows already keeps answers most "why did it crash" questions without symbols or a debugger. Doing more would mean a large dependency, a security surface (`dbgeng` loads plugins), and a symbol-server EULA, all for diminishing returns. Design investigation: `docs/research/CRASH_DIAGNOSTICS.md`.
**Status** Active (v1). Fast-follow: minidump `ModuleList` fault attribution in the default (non-elevated) path where the dump is user-readable; `Win32_ReliabilityRecords` timeline.

## D-021. Portable distribution: self-contained folder build, VC++ runtime bundled
**Date** 2026-09-03
**Decision** The "send a friend" build is `dotnet publish -c Release -r win-x64 --self-contained -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None`, a **folder** (not single-file). An `IncludeVcRuntime` MSBuild target copies `vcruntime140*`/`msvcp140*`/`concrt140` next to the exe from the VS redist dir (System32 fallback). Zipped inside one `PowerX/` folder with a `READ ME FIRST.txt`. Not code-signed yet (SmartScreen "Run anyway").
**Why** A self-contained WinUI 3 publish does **not** include the VC++ 2015-2022 runtime that native `Microsoft.UI.Xaml.dll` links against; on a clean PC the app dies silently during MainWindow XAML load. Single-file publish *works* but is fragile across machines (extraction to `%TEMP%\.net`, AV interference), a friend's first report was exactly this failure mode. The 500-file folder is uglier but reliable, and the user only ever double-clicks one exe.
**Status** Active. `src/PowerX.App/PowerX.App.csproj` `IncludeVcRuntime` target. Revisit when the app is code-signed and an installer exists.

## D-020. RunOnce startup entries: never a fake toggle
**Date** 2026-09-03
**Decision** `StartupProvider.SetEnabled` refuses RunOnce entries (returns a failure explaining they run once at next sign-in and aren't governed by `StartupApproved`); `CanToggle()` lets the UI disable the switch. `Remove(entry)` / `CanRemove(entry)` deletes a RunOnce value (stashing name+value+hive to `HKCU\SOFTWARE\PowerX\RemovedRunOnce` for manual recovery), surfaced as a confirm-gated "Remove entry" in the Startup page's... menu.
**Why** Windows ignores `StartupApproved\Run` for `RunOnce`, so the previous code wrote a "disabled" marker that did nothing while the UI claimed success, a fake-success violation of the honesty rules. Deleting the value is the only real way to stop it; the backup keeps it recoverable.
**Status** Active.

## D-019. Theme options: window material + accent, no UI-density toggle (yet)
**Date** 2026-09-02
**Decision** Settings > Appearance offers theme (System/Light/Dark), **window material** (Mica / Acrylic / None-solid) and an **accent colour** from 8 presets. Material applies live via `Window.SystemBackdrop`; when "None", `ApplyBackdrop` paints the root panel with an opaque theme brush so the content area isn't left unpainted. Accent overrides all six `SystemAccentColor{Light,Dark}{1..3}` tint resources at launch (before first brush resolution) by lerping the base toward white/black; it takes effect on restart. No compact/comfortable density toggle. WinUI 3 has no supported compact metrics and hand-scaling every control is fragile.
**Why** Material + accent are the high-impact, low-risk parts of "theming". Density is deferred until there's a supported mechanism.
**Status** Active. `AppSettings.Backdrop` / `.Accent`.

## D-018. Optimization profiles are visible tweak sets, never hidden scripts
**Date** 2026-09-02
**Decision** `OptimizationProfile` is `(Id, Name, Description, Tone, TweakIds[])`. Built-ins: `recommended`, `privacy`, `lowspec` ("Potato mode"), `gaming`, `restore` (empty list, special-cased to revert everything currently applied). Applying one always shows a preview diff (what will change vs. the live machine state), offers an optional restore point, then runs `TweakEngine.ApplyMany` as one transaction. A test asserts every referenced tweak resolves and is not `SecurityTradeoff`/`Destructive`, so no profile can ever weaken a security boundary. Same model drives `powerx profile list|show|apply`.
**Why** The safety loop is detect, show, apply, verify, log, undo. A profile is a convenience, not a black box; the user sees and confirms every change.
**Status** Active. `src/PowerX.Core/Tweaks/OptimizationProfile.cs`.

## D-017. Update check: a version.json in the repo, no auto-download
**Date** 2026-09-02
**Decision** `version.json` at the repo root holds `{version, published, notes, url, minimumWindowsBuild, installerUrl, installerSha256, installerBytes}`. `UpdateChecker` fetches it from `raw.githubusercontent.com/Nowalski/Power-X/main/version.json`, compares it to the running assembly version and surfaces the result as a dismissible `InfoBar` in the app, `powerx update` in the CLI. Auto-check is once/day, opt-out in Settings, dismissed version not re-nagged.
**Updated (D-023):** when the manifest carries a hash-pinned installer (`installerUrl` on `github.com`/`objects.githubusercontent.com` over HTTPS, a 64-hex `installerSha256`, a positive `installerBytes`), the app offers **Download & install**, `UpdateInstaller` downloads to `%LOCALAPPDATA%\PowerX\update`, verifies size + SHA-256, refuses to run on any mismatch, then launches `msiexec /i` (self-elevating in-place upgrade) and exits. CLI: `powerx update --download`. Without those fields it still just opens the releases page. This is not "download a mystery binary", the URL host is allow-listed, the hash is pinned in a manifest served over HTTPS from the project's own repo.
**Why** A passive link is safe but the folder-vs-MSI switch (D-023) made a proper in-app updater worth the small, well-bounded surface: allow-listed host + pinned hash + explicit user consent + verify-before-run.
**Status** Active. The check 404s until `version.json` lands on `main`; installer fields are empty until the first tagged release.

## D-015. Registry-tweak "absent value" means default, not "Custom"
**Date** 2026-09-02
**Decision** `RegistryTweakOperation.Detect` treats a missing registry value as matching the Windows default (unless the tweak's *applied* state is itself "value absent"). Previously an absent value matched neither the concrete `AppliedValue` nor the concrete `DefaultValue` and reported `Custom`, which the UI left un-togglable.
**Why** Most Windows toggles ship with the value absent; the old behaviour made ~half the catalog appear stuck.
**Status** Active. Multi-value tweaks can still legitimately be `Custom` when partially set.

## D-016. Debloat removes for all users + deprovisions (we run elevated)
**Decision** Curated non-`KeepSystem` entries use `RemovePackageAsync(RemoveForAllUsers)` + `DeprovisionPackageForAllUsersAsync`. Un-catalogued packages fall back to the system-signature gate. A small `KeepSystem` list (Store, Security, shell frameworks, Terminal/Notepad/Paint/Snipping) is never removable.
**Why** Inbox consumer apps are system-signed; per-user removal alone left them greyed out and they'd return. This is the DISM-equivalent path, done safely per-package with confirmation.
**Status** Active.

## D-014. App runs elevated (manifest `requireAdministrator`)
**Date** 2026-09-02
**Decision** `app.manifest` requests `requireAdministrator`. PowerX manages system state (process control, services, HKLM tweaks), a split broker is still the long-term goal (D-ARCH) but until it exists the app runs elevated so every feature works. Every launch shows one UAC prompt.
**Status** Active. `ProcessActions` / `QuickActions` still surface access-denied gracefully for protected processes.

## D-013. Migrated to.NET 10 (SDK 10.0.400) once installed on the build host
**Date** 2026-09-02
**Decision** All projects target `net10.0-windows` (`net10.0-windows10.0.19041.0` for `PowerX.App`); `Microsoft.Extensions.*` at 10.0.0, `Microsoft.WindowsAppSDK` at 1.8. `global.json` pins 10.0.400 with `rollForward: latestFeature`.
**Why** D-004 always planned this; .NET 10 is the intended target from the brief. Build, tests and the WinUI x64 Release all pass on .NET 10.
**Note for local dev:** the machine also has an SDK-less `C:\Program Files (x86)\dotnet` that can win PATH order and produce "No .NET SDKs were found". Put `C:\Program Files\dotnet` first on PATH (or remove the x86 entry).
**Status** Active.

## D-012. WinUI 3 app builds unpackaged via `dotnet build` (no VS, no workload)
**Date** 2026-09-02
**Decision** `PowerX.App` targets `net10.0-windows10.0.19041.0`, `WindowsPackageType=None`, `WindowsAppSDKSelfContained=true`, `Platforms=x64;arm64`. It restores and builds green on this host with only the.NET 9 SDK, the WindowsAppSDK NuGet carries its own XAML compiler.
**Consequence** `PowerX.App` is **not** in `PowerX.sln` (the solution stays AnyCPU for Core/CLI/Tests). CI builds it as a separate `-p:Platform=x64` step. INPC rows are hand-rolled (the CommunityToolkit source-gen for partial properties didn't emit under this SDK; plain `[ObservableProperty]` fields warn MVVMTK0045 for WinRT marshalling, so we dropped the dependency).
**Status** Active. The app builds with zero warnings.

## D-011. CLI ships first, WinUI shell scaffolded alongside
**Date** 2026-09-02
**Decision** Milestone 1 delivers a working `PowerX.Core` + `powerx` CLI on real Windows telemetry, with the WinUI 3 app added as a scaffold that consumes the same Core services.
**Why** The build host has the.NET 9 SDK but no Visual Studio and no interactive desktop session, so a GUI cannot be visually QA'd here. The Core/CLI slice is fully buildable, testable and runnable now; it also satisfies the prompt's rule that GUI and CLI call the same underlying functionality. GUI polish happens on a machine where it can be seen.
**Status** Done. The CLI shipped in 0.1.0 alongside the app.

## D-010. Tweak state model: Default / Applied / Custom / NotApplicable / Unknown
**Decision** Detection compares the live registry value against both the shipped Windows default and our desired value. Anything else is `Custom` (do not silently overwrite).
**Why** Users and other tools also change these keys. Reporting `Custom` honestly and requiring an explicit apply is safer than assuming.
**Status** Implemented in `RegistryTweakOperation`.

## D-009. Every mutation goes through `TweakEngine.Execute` and is logged
**Decision** No registry writes in UI/CLI handlers. `Execute` runs detect, a privilege check, a build check, apply and verify, then appends a `ChangeRecord` to `%LOCALAPPDATA%\PowerX\change-history.jsonl`.
**Why** Enables undo, history timeline, CLI parity, dry-run, and testability. Single audited path.
**Status** Implemented.

## D-008. Providers return `ProviderResult<T>` with an explicit quality signal
**Decision** `Reliable` / `Approximate` / `Unavailable`. Consumers must render "unavailable", never fabricate `0`.
**Why** A fabricated `0` or a fake `0 C` reading is a lie about the machine. Say "unavailable" instead.
**Status** Implemented for CPU and memory.

## D-007. Process enumeration via a single `NtQuerySystemInformation(SystemProcessInformation)`
**Decision** One syscall per refresh, delta-based CPU% and I/O rate against the previous snapshot, rather than per-process Win32 handles.
**Why** This is how Task-Manager-class tools stay cheap under process storms. Opening thousands of handles per second does not scale.
**Trade-off** Struct layout is community-documented, not fully in `winternl.h`. Guarded by tests that assert the current process is found and all values are in range. Per-process image path / signature / user need a follow-up handle and are resolved lazily.
**Status** Implemented, with telemetry tests.

## D-006. CPU%: `GetSystemTimes` for total, `NtQuerySystemInformation(ProcessorPerformance)` for per-core
**Why** Documented, cheap, no PDH/WMI dependency or counter-corruption risk. PDH stays available as a later provider for counters these APIs don't expose.
**Status** Implemented.

## D-005. Central package management + `Directory.Build.props`; pin SDK via `global.json`
**Status** Done.

## D-004. Language/runtime: C# on.NET (now.NET 10, see D-013)
**Why** `net10.0-windows` gives full Win32/registry access and source-generated P/Invoke (`LibraryImport`) with zero extra packages. No native C++/Rust component until profiling justifies one.
**Status** Superseded by D-013.

## D-003. Hand-written `LibraryImport` P/Invoke, not CsWin32 (for now)
**Why** Keeps the interop surface tiny, readable and reviewable. CsWin32 can be introduced if the surface grows large.
**Status** Active. See `src/PowerX.Core/Interop/`.

## D-002. Licence: MIT, with a hard rule against importing GPL or restricted code
**Why** Maximises reuse and contribution. Winhance's no-compete clause and GPL projects mean we study behaviour/UX only and reimplement from Microsoft docs. See `docs/research/LICENSE_REVIEW.md`.
**Status** Active. `LICENSE` is MIT as of 0.1.0.

## D-001. Product framing: "Windows Control Center", not "debloater"
**Why** Monitor / Diagnose / Optimize / Configure / Clean / Repair / Manage / Understand. The tool must be useful to someone who never debloats.
**Status** Active, drives the information architecture in `docs/PRODUCT_SPEC.md`.
