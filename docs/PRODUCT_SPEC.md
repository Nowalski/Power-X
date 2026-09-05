# PowerX, product spec

## What it is
A native Windows control center: monitor, diagnose, optimize, configure, clean, repair, manage software, and understand Windows. Useful even to someone who never debloats.

## Principles

1. **Information design over data dumping.** Hierarchy, grouping, heat, sparklines and summaries, not 70 columns.
2. **Progressive disclosure.** A simple default view, with the expert drill-down always one step away. Simple is not crippled.
3. **Tell the truth.** Real telemetry or "unavailable". No invented FPS claims. Honest about reversibility.
4. **Least unnecessary software**, not the lowest process count.
5. **Safer than the alternatives.** Detect, record, plan, show, apply, verify, log, undo.
6. **The tool must not be the resource hog.**
7. **Works offline. No telemetry.**

## Information architecture

- **Home**: a live overview (CPU, GPU, RAM, disk, network, uptime, top processes, recommendations, active profile). Each card drills into its page.
- **Processes**: a tree with grouping, live columns, resource heat, search, stable selection, and a process inspector.
- **Performance**: CPU, Memory, GPU, Network, each capability-detected.
- **Startup**: Run and RunOnce keys, startup folders, scheduled tasks and services. Enable and disable first, not delete.
- **Services**: a modern services view for the common workflows, with dependency awareness and warnings on critical services.
- **Network activity**: a per-process endpoint list, filterable by process. No automatic remote lookups.
- **Debloat**: installed packages by category, with identity, scope, reversibility and a risk class. Nothing high-risk is preselected.
- **Tweaks**: the declarative catalog, searchable, with the explanation and evidence for each item.
- **Cleanup**: transparent, size-first, itemised.
- **Repair**: front ends for DISM, SFC, CHKDSK, DNS and network resets, with streamed output that does not block the UI.
- **Crash insights**: what Windows already recorded about recent crashes and hangs, with the observed facts kept separate from the likely cause.
- **Change history**: a timeline of what PowerX changed, with "undo compatible changes".

Global: a **search** box (processes, settings, tweaks, services, apps, pages) and a **command palette** (Ctrl+K) that obeys the same safety rules.

## Every tweak answers the same questions
What does this do? Why might I want it? What is the downside? Is a restart needed? Can I undo it? Is it recommended? Enforced by `TweakDefinition` required members and a catalog test.

## Profiles
Recommended, Privacy, Potato mode (low-spec), Gaming (evidence-backed only), and Restore defaults. A profile is a visible set of individual tweaks; the full diff is shown before it applies. Security features are never turned off by a profile.

## Out of scope
Piracy, activation bypass, licence circumvention. Detection-evasion tooling. "Disable Defender for FPS" as a recommendation.

## Status
Version 0.1.19 is out: `PowerX.Core`, the `powerx` CLI, and the WinUI app with live dashboards, the tweak catalog and profiles, debloat, startup, services, cleanup, repair and crash insights.

Added since 0.1.0: a health check that ranks what is worth doing most impactful first; per-adapter GPU metrics on multi-GPU machines; a temperatures page (ACPI thermal zones and per-disk sensors); a network view with listening ports and per-process connections; scheduled tasks, drivers and firewall rules; an event log browser; a Security page; a storage explorer; a system report; an append-only change history; and a config import and export format (`powerx.config/1`, via Settings or `powerx config export|import`) that lists which tweaks are applied and which curated apps were removed, carries no machine or user detail, and shows the full plan before it applies anything.

Next up: an elevated broker so the GUI can run without a full-time admin token.
