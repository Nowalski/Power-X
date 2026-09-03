# Architecture

## Shape

```
┌─────────────────────────────┐     ┌──────────────────────────────┐
│  PowerX.App  (WinUI 3)      │     │  PowerX.Cli   (powerx.exe)   │
│  MVVM, NavigationView, Mica │     │  Spectre.Console rendering    │
└──────────────┬──────────────┘     └───────────────┬──────────────┘
               │        both depend only on         │
               ▼                                    ▼
        ┌────────────────────────────────────────────────────┐
        │                 PowerX.Core                        │
        │  Telemetry  · Processes · Tweaks · Transactions    │
        │  Diagnostics · Configuration · (Cleanup/Repair…)   │
        └───────────────┬───────────────────────┬────────────┘
                        │                       │
             documented Win32 / NT        elevated helper (future)
             LibraryImport, registry      PowerX.Host  — brokered,
             GlobalMemoryStatusEx,        whitelisted actions over a
             NtQuerySystemInformation     named pipe, param-validated
```

**Rule:** GUI and CLI never contain system logic. They render `PowerX.Core` results and call `PowerX.Core` services. Anything else is a bug (see DECISIONS D-009).

## Projects

| Project | TFM | Role |
|---|---|---|
| `PowerX.Core` | `net10.0-windows` | All telemetry, models, tweak engine, change log. No UI deps. |
| `PowerX.Cli` | `net10.0-windows` | `powerx` command-line front end. |
| `PowerX.App` | `net10.0-windows10.0.19041.0` | WinUI 3 desktop app (WindowsAppSDK 1.8, unpackaged). |
| `PowerX.Core.Tests` | `net10.0-windows` | xUnit + FluentAssertions. |
| `PowerX.Host` | *future* | Minimal elevated broker for admin-scoped actions. |

## Core subsystems

### Telemetry — `PowerX.Core.Telemetry`
Provider objects, one metric domain each: `CpuMetricsProvider`, `MemoryMetricsProvider`, (planned) `GpuMetricsProvider`, `DiskMetricsProvider`, `NetworkMetricsProvider`, `SensorMetricsProvider`.

- Every provider returns `ProviderResult<T>` = `{ Quality, Value?, Detail? }`.
- Stateful providers (CPU, processes) keep the previous sample for delta math and are called on a fixed cadence.
- **Sampling cadence ≠ render cadence.** Core samples at 1 s (configurable); the GUI interpolates for animation. When the window is hidden the cadence backs off (prompt §32).

### Processes — `PowerX.Core.Processes`
`ProcessProvider.Enumerate()` → one `NtQuerySystemInformation` call → `ProcessSnapshot`. CPU% / I/O-rate derived from deltas. `BuildTree()` produces the parent→children map for the tree view. Lazy enrichment (path, publisher, signature, user, modules, handles) via per-process queries, cached.

### Tweaks — `PowerX.Core.Tweaks`
Declarative. `TweakDefinition` (metadata + the four user-facing questions + evidence + build range + risk + restart scope) owns an `ITweakOperation` (`Detect/Apply/Revert/Verify`, idempotent). `RegistryTweakOperation` covers the registry case with a list of `RegistryValueSpec` (`AppliedValue` / `DefaultValue`; null default ⇒ revert deletes). `TweakCatalog` is the curated set. `TweakEngine` is the only mutation entry point.

### Transactions — `PowerX.Core.Transactions`
`ChangeRecord` (immutable audit row) → `ChangeLog` (JSON-lines at `%LOCALAPPDATA%\PowerX\`). `TransactionResult` aggregates a batch (`Succeeded / AlreadyConfigured / Failed`) and the combined `RestartScopeSummary`. Powers the history timeline and "undo compatible changes".

### Diagnostics — `PowerX.Core.Diagnostics`
`SystemInfoProvider.Collect()` (edition, build+UBR, arch, install date, CPU, RAM), `PrivilegeCheck.IsElevated()`. "Copy System Report" builds on this with opt-in redaction.

## Privilege model (target)
GUI runs non-elevated. Admin-scoped operations are brokered to `PowerX.Host` (auto-elevated once, or installed as a service for portable-vs-installed parity). The broker exposes **structured, whitelisted** actions — never "run this command". Every parameter validated against the tweak/action catalog. Pipe peer identity checked.

## Error handling
Providers and operations never throw across the API boundary — they return `Unavailable` / `TweakOutcome.Fail`. The UI shows a plain sentence; the HRESULT/stack goes to the log and a "technical details" disclosure.

## Testing seams
- Providers are plain classes, newable in tests (telemetry tests run against the live machine and assert ranges/invariants).
- `TweakEngine` takes an `IEnumerable<TweakDefinition>` and a `ChangeLog` path — tests use a throwaway `HKCU\Software\PowerX.Tests\<guid>` subtree and a temp log.
- `RegistryTweakOperation` is the unit under test for round-trip / idempotency / dry-run.
