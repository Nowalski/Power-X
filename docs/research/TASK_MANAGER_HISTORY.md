# Task Manager design history, principles we carry forward

Sources: Microsoft "Building Windows 8" blog (the Task Manager redesign posts by the In-The-Box / Fundamentals team, 2011), subsequent Windows blog posts, and observable behaviour of Win8 to Win11 Task Manager.

## The Windows 8 redesign, what changed and why

**Problem they identified:** the old Task Manager tried to serve everyone equally and served no one well. Telemetry showed the overwhelming majority of launches were for a handful of tasks.

**The common scenarios (their data, still true):**
1. "An app is hung, kill it."
2. "My PC is slow, what's using it up?"
3. "What is all this stuff running?"
4. Check/So manage what runs at startup.

**Design responses we adopt:**

| Their move | Our application of it |
|---|---|
| Default **"fewer details" view**: just apps, one click to End task | Processes page opens grouped by app, noise collapsed; End task is the primary action |
| **Heat map**, colour intensity on resource cells so hogs jump out | `Format.Heat` today (CLI); GUI uses subtle background intensity, light/dark/high-contrast safe, never colour-only |
| Merged **Startup** into Task Manager with a "startup impact" rating | Startup center; impact shown when measurable, "not measured" otherwise |
| **Friendly names + icons** instead of `svchost.exe x12` | Group svchost by hosted services; show the service list in the inspector |
| Resource columns show **absolute + %**, summary heat at column header | Same; header shows total, cells show share |
| "**More details**" expands to the full technical view, nothing removed | Progressive disclosure everywhere; expert columns, inspector tabs |
| Per-app **history** (Alt+tab-style resource usage over time for Store apps) | Resource timeline feature idea |

## Later evolution (Win10 then Win11)
- **Win10**: GPU columns + GPU engine breakdown; DPI/units cleanup; disk % ; power-usage columns.
- **Win11 22H2+**: WinUI Mica shell; **Efficiency mode** (EcoQoS, caps a process to efficiency cores / lower scheduling priority); dark mode; search box; process filtering.
- **Recent**: per-process power/energy, "don't dim on lock" style polish.

## Principles distilled
1. **Optimize the common path, don't amputate the expert path.**
2. **Design is information hierarchy**, decide what matters, show that first.
3. **Names and grouping** beat raw process lists.
4. **The tool runs when the PC is already sick**, cheapness and resilience are features (,).
5. **Stable rows.** Rapid metric updates must not make the list unclickable, sort snapshots, don't reorder on every tick unless asked.
