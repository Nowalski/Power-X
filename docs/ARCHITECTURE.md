# Architecture

## Shape

```
  PowerX.App  (WinUI 3)              PowerX.Cli  (powerx.exe)
  MVVM, NavigationView, Mica         Spectre.Console rendering
        |                                   |
        +--------- both depend only on ------+
                          |
                    PowerX.Core
   Telemetry, Processes, Tweaks, Transactions, Diagnostics,
   Configuration, Cleanup, Repair
                          |
              documented Win32 / NT interop
        LibraryImport, registry, GlobalMemoryStatusEx,
        NtQuerySystemInformation
```

The GUI and CLI never contain system logic. They render `PowerX.Core` results and call `PowerX.Core` services. Anything else is a bug (see DECISIONS D-009).

## Projects

| Project | Role |
|---|---|
| `PowerX.Core` | All telemetry, models, the tweak engine, the change log. No UI dependencies. |
| `PowerX.Cli` | The `powerx` command-line front end. |
| `PowerX.App` | WinUI 3 desktop app (WindowsAppSDK 1.8, unpackaged, x64). |
| `PowerX.Core.Tests` | xUnit and FluentAssertions. |

All projects target `net10.0-windows`; the WinUI app pins `net10.0-windows10.0.19041.0`.

## Core subsystems

### Telemetry, `PowerX.Core.Telemetry`
One provider per metric domain: `CpuMetricsProvider`, `MemoryMetricsProvider`, `GpuMetricsProvider`, `NetworkMetricsProvider`, and the process sampler.

- Every provider returns `ProviderResult<T>`, which carries a quality signal, an optional value and an optional detail string.
- Stateful providers (CPU, processes) keep the previous sample for delta maths and are called on a fixed cadence.
- Sampling cadence is not the same as render cadence. Core samples once a second by default; the GUI interpolates for animation. When the window is hidden the cadence backs off.

### Processes, `PowerX.Core.Processes`
`ProcessProvider.Enumerate()` makes one `NtQuerySystemInformation` call and returns a snapshot. CPU percent and I/O rate come from deltas against the previous snapshot. `BuildTree()` produces the parent-to-children map for the tree view. Path, publisher, signature, user, modules and handles are resolved lazily per process and cached.

### Tweaks, `PowerX.Core.Tweaks`
Declarative. A `TweakDefinition` holds the metadata, the user-facing explanations, the evidence, the build range, the risk class and the restart scope, and owns an `ITweakOperation` with idempotent `Detect`, `Apply`, `Revert` and `Verify`. `RegistryTweakOperation` covers the registry case with a list of value specs (applied value and default value; a null default means revert deletes the value). `TweakCatalog` is the curated set. `TweakEngine` is the only place a tweak is applied.

### Transactions, `PowerX.Core.Transactions`
A `ChangeRecord` is one immutable audit row. `ChangeLog` appends them as JSON lines under `%LOCALAPPDATA%\PowerX`. A `TransactionResult` aggregates a batch (succeeded, already configured, failed) and the combined restart scope. This powers the history timeline and "undo compatible changes".

### Diagnostics, `PowerX.Core.Diagnostics`
`SystemInfoProvider.Collect()` returns the edition, build and revision, architecture, install date, CPU and RAM. `PrivilegeCheck.IsElevated()` reports elevation. The crash-insights readers live here too (see DECISIONS D-022). "Copy system report" builds on this with opt-in redaction.

## Error handling
Providers and operations never throw across the API boundary. They return `Unavailable` or a failure result. The UI shows a plain sentence; the HRESULT and stack go to the log and a "technical details" disclosure.

## Testing seams
- Providers are plain classes, newable in tests. Telemetry tests run against the live machine and assert ranges and invariants.
- `TweakEngine` takes an `IEnumerable<TweakDefinition>` and a change-log path. Tests use a throwaway `HKCU\Software\PowerX.Tests\<guid>` subtree and a temp log.
- `RegistryTweakOperation` is the unit under test for round-trip, idempotency and dry-run behaviour.
