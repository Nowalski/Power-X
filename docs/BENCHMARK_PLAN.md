# Benchmark plan

Two goals: (1) PowerX itself respects the machine; (2) any performance claim is backed by reproducible numbers.

## 1. PowerX resource budgets

Measured with PowerX on the default Home view, machine otherwise idle, 5 min window, compared against Task Manager and System Informer on the same machine.

| Metric | Aspirational target (idle, Home visible) | Hard ceiling | How measured |
|---|---|---|---|
| CPU | < 1% avg | < 3% avg | our own per-process sampler + PerfMon cross-check |
| Private working set | < 200 MB | < 350 MB | `GetProcessMemoryInfo` |
| Disk I/O | ~0 sustained (log writes only on change) | no sustained writes | ETW Kernel-Disk / Resource Monitor |
| Network | 0 sustained | 0 unless user opens Software/Discover | GetIfTable2 delta |
| Handles | stable (no growth over 1 h) | no leak | handle count trend |
| Startup to interactive Home | < 1 s on NVMe + modern CPU | < 2.5 s | stopwatch from process start to first frame |
| Hidden/minimised | sampling backs off to ≥ 5 s; CPU ≈ 0 | | |

Regression gate: CI (or a nightly perf job on a real box) fails if working set or idle CPU exceeds the previous tagged release by > 15%.

## 2. Telemetry cost micro-benchmarks (BenchmarkDotNet, `PowerX.Benchmarks`, later)
- `ProcessProvider.Enumerate()` — target < 5 ms for ~400 processes; must not allocate per-row beyond the snapshot list.
- `CpuMetricsProvider.Sample()` — target < 0.5 ms.
- Tree build for 400 processes — < 1 ms.
- Search across catalog + processes + services — < 10 ms.

## 3. Resilience scenarios (prompt §38)
Run the enumerator + UI under: 100% CPU (stress), memory pressure (fill to commit limit), 2000+ synthetic processes, disk contention. PowerX must stay interactive and must not OOM. Large lists virtualized; stale async work cancelled.

## 4. Tweak / profile effect benchmarks (prompt §66)
For any profile or performance tweak we describe as beneficial:
- Report **process count**, **idle CPU**, **committed memory**, **startup-app count**, **boot-time markers** (`Microsoft-Windows-Diagnostics-Performance` event 100) before and after.
- ≥ 5 runs each side, report mean ± stdev, clean-ish state (reboot between).
- **Never** publish an FPS delta without: fixed clocks where possible, ≥ 3 titles, ≥ 5 runs, frametime percentiles (1%/0.1% low), variance shown.
- If Δ ⊂ noise: the catalog entry says "no measurable performance benefit on test hardware".

Honest framing for most debloating: fewer unwanted apps, fewer background components, less distraction, better privacy, cleaner startup, lower background resource use — not FPS.

## Test hardware log
| Box | CPU | RAM | Disk | GPU | Windows |
|---|---|---|---|---|---|
| dev-1 | Ryzen 7 9800X3D (16T) | 64 GB | NVMe | (tbd) | 11 Home 26200.9168 (25H2) |
| _add VM matrix per QA_PLAN_ | | | | | |
