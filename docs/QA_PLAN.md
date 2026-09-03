# QA plan

## Test layers
| Layer | Where | Runs in CI |
|---|---|---|
| Unit | `PowerX.Core.Tests` — tweak state machine, config serialization, risk rules, metric math, tree build, version gating | Yes |
| Provider invariants | same — live-machine sampling asserts ranges/invariants (current process present, values 0–100, totals consistent) | Yes (windows runner) |
| Integration (registry) | throwaway `HKCU\Software\PowerX.Tests\<guid>` subtree; apply/revert/idempotency/dry-run | Yes |
| Integration (services / winget / process actions) | isolated, opt-in, `[Trait("Category","Elevated")]` / `("Category","External")` | Nightly / manual |
| UI automation | critical flows via WinAppDriver / UIA once GUI lands | Nightly |
| Performance | BenchmarkDotNet + idle-budget job on a real box | Nightly |
| Manual visual QA | checklist below | Per PR touching UI |

## Windows VM test matrix (prompt §50)
Automated snapshots; destructive tweaks never tested on the dev workstation.

| OS | Build track | Editions | Priority |
|---|---|---|---|
| Windows 11 | current stable (23H2 / 24H2 / 25H2 as applicable) | Home, Pro | P0 |
| Windows 11 | Insider (Dev or Beta) | Pro | P1 — catch build-gated tweak breakage early |
| Windows 10 | 22H2 (if we commit to supporting it) | Home, Pro | P2 |

Per VM: fresh snapshot → run full `powerx scan` → apply each profile → revert → assert state returns to baseline → assert no unexpected changes (registry diff).

## Visual QA checklist (prompt §51) — run per UI PR
Scaling: **100 / 125 / 150 / 200%**. Window: small / default / maximized / ultrawide. Theme: **dark / light / high-contrast**. Motion: normal / reduced.

Look for: clipped labels · awkward cards · uneven margins · oversized controls · bad text wrap · inconsistent radius · wrong icon sizes · table density · chart readability · hover defects · animation jank · focus visibility · keyboard traps.

Capture screenshots of Home + Processes at 100% dark and 150% light every milestone; diff against the previous set.

## Continuous audit loop (prompt §72) — per milestone
Grep and triage: `TODO` · `FIXME` · `throw new NotImplemented` · `// mock` / hardcoded sample data · disabled controls with no reason · empty `catch {}` · `.Result` / `.Wait()` on the UI thread · `Task` not awaited · missing `CancellationToken` · per-row `OpenProcess` in a hot loop · registry writes outside `TweakEngine` · tweaks missing build range / revert / evidence · missing automation names · design tokens not from the palette.

## Definition of done for a system action (prompt §71)
Detection reliable? · Apply idempotent? · Revert reliable & tested? · UI shows real state? · Windows build checked? · Logged? · Risk classified? · Failure handled with a plain message? · Documented in `TWEAK_CATALOG.md`? · Has a round-trip test?
If any answer is no → not done.

## Installer smoke (D-023) — per build shared for testing
1. `installer\build.ps1` → `publish\PowerX-Setup-<ver>-win-x64.msi`; note the printed sha256 / bytes.
2. Confirm the publish folder has `vcruntime140.dll` / `msvcp140.dll` / `concrt140.dll` (the `AddVcRuntimeToPublish` target ran).
3. Decompile check: `wix msi decompile <msi>` → exactly one `<Shortcut>`, in `AppShortcutFolder` (Start menu), **no `DesktopFolder`**.
4. On a **clean VM without the VC++ redistributable and without Visual Studio**: double-click the MSI, accept UAC + SmartScreen → wizard installs to `Program Files\PowerX`; Start-menu entry appears; **no desktop shortcut**; Overview window renders; `%LOCALAPPDATA%\PowerX\crash.log` absent; every nav entry opens without throwing.
5. Re-run the MSI (same version) → repairs / no-ops cleanly. Build a `+0.0.1` MSI → installs over the top (major upgrade), old files gone, settings kept.
6. Add/Remove Programs → Uninstall → `Program Files\PowerX` and the Start-menu folder are removed; `%LOCALAPPDATA%\PowerX` (settings/history) is left.
7. Updater: with a real `version.json` carrying `installerUrl`/`installerSha256`/`installerBytes`, the in-app "Download & install" verifies and launches; a deliberately wrong `installerSha256` must be refused ("SHA-256 does not match … It was NOT run").

## Portable single-file smoke (secondary)
`dotnet publish ... -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true` → one `PowerX.App.exe` (+ loose VC runtime DLLs). Same clean-VM launch check as above.

## Regression coverage added (audit 8th pass)
- `RegistryTweakOperation`: absent value → `Default`; ACL-denied read → `Unknown` (not `Default`); `TweakEngine.Execute` survives a throwing `Detect`.
- `InstalledPrograms.SplitCommand`: quoted / unquoted-with-spaces / MSI / bare-exe / rundll32 forms.
- `ChangeLog.RevertableChanges`: reverted / failed / no-op / ended-Custom applies are all excluded.

## Manual checks still owed (no automated coverage)
- Efficiency-mode on→off restores the exact prior priority class (needs a real elevated process).
- Windows Update `Disable` then `Restore` returns service Start types and AU policy to their pre-`Disable` values (snapshot round-trip) — verify on a VM with a non-default `wuauserv` Start.
- `SystemRestore.Create` leaves `SystemRestorePointCreationFrequency` unchanged from before the call.
- Process inspector "Integrity" row shows a real level (Medium for a normal app, High when elevated).
- Startup page: a RunOnce row's toggle is disabled with the explanatory line.

## Crash diagnostics
Design only — see [`research/CRASH_DIAGNOSTICS.md`](research/CRASH_DIAGNOSTICS.md). No code, no QA yet; a `D-0xx` decision is required first.

## Release gate
CI green (build + analyzers + all non-manual tests) · visual checklist signed for the milestone · portable smoke on a clean VM · perf budgets within 15% of last tag · `THIRD-PARTY-NOTICES.md` regenerated · `DECISIONS.md` current.
