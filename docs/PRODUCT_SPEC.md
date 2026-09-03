# PowerX — product spec

## What it is
A native Windows control center: **Monitor · Diagnose · Optimize · Configure · Clean · Repair · Manage software · Understand Windows.** Valuable even to someone who never debloats.

## Principles (non-negotiable)
1. **Information design over data dumping.** Hierarchy, grouping, heat, sparklines, summaries — not 70 columns.
2. **Progressive disclosure.** Simple default view; expert drill-down always reachable. Simple ≠ crippled.
3. **Tell the truth.** Real telemetry or "unavailable". No fake FPS claims. Honest reversibility.
4. **Least unnecessary software** — not lowest process count (prompt §74).
5. **Safer than the alternatives.** Detect → record → plan → show → apply → verify → log → undo.
6. **The tool must not be the resource hog.** Idle budgets in `BENCHMARK_PLAN.md`.
7. **Works offline. No telemetry by default.**

## Information architecture
- **Home** — live overview (CPU / GPU / RAM / disk / network / uptime / top processes / recommendations / active profile). Cards drill into their page.
- **Processes** — tree + grouping, live columns, heat, search, stable selection, process inspector on activate.
- **Performance** — CPU, Memory, GPU, Storage, Network, Sensors (each capability-detected).
- **Startup** — folders, Run/RunOnce, scheduled tasks, services, shell extensions; enable/disable first, not delete.
- **Services** — modern `services.msc` for the common workflows; dependency awareness; critical-service warnings.
- **Network activity** — TCPView-style endpoint list, filter by process; no auto remote lookups.
- **Debloat** — installed packages by category with identity, scope, reversibility, risk class. Nothing high-risk preselected.
- **Tweaks** — the declarative catalog, searchable, with the four questions + evidence per item.
- **Cleanup** — transparent, size-first, itemised.
- **Repair** — DISM / SFC / CHKDSK / DNS / Winsock front ends, streamed output, non-blocking.
- **Software** — WinGet-backed Installed / Updates / Discover.
- **System info** — full report + "Copy System Report" with redaction.
- **Change history** — timeline of what PowerX changed; "undo compatible changes".

Global: **search** (processes, settings, tweaks, services, apps, pages, docs) and a **command palette** (Ctrl+K) that obeys the same safety system.

## The four questions — every tweak answers them
What does this do? · Why might I want it? · What's the downside? · Restart needed? · Can I undo it? · Is it recommended?
Enforced by `TweakDefinition` required members and a catalog test.

## First run
A calm scan ("Understanding your PC…") → an overview with counts → **Review recommendations**, never "Debloat everything". Recommendations are deterministic and explainable; dismissible.

## Profiles
Recommended · Privacy · Gaming (evidence-backed only) · Minimal Windows · Developer (preserves WSL/Hyper-V/debugging) · Restore Windows Defaults. A profile is a visible set of individual tweaks; the full diff is shown before apply. Security features are never disabled by default.

## Out of scope
Piracy, KMS/activation bypass, license circumvention. Detection-evasion tooling. "Disable Defender for FPS" as a recommendation.

## Milestones
1. **Core + CLI + telemetry slice** ← *current.* Home/Processes/CPU/Memory data, tweak engine, history, tests.
2. GPU / Disk / Network providers + process inspector + WinUI Home & Processes.
3. Profiles, config import/export, recommendations engine.
4. Debloat + Startup + Services.
5. Package manager + Cleanup + Repair.
6. Elevated broker, installer + portable, signing, docs site.
