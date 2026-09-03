# Competitor / reference matrix

Studied for ideas, weaknesses and risky patterns. **We do not copy code or visual identity.** Licence notes in `LICENSE_REVIEW.md`.

## Configuration / debloat

| Project | Strengths to learn from | Weaknesses / risks to avoid | Licence posture |
|---|---|---|---|
| **Raphire/Win11Debloat** | Clear presets; sane defaults; strong "explain each action" ethos; PowerShell, auditable; good `Sophia`-style reversibility for many items | Script UX (menu prompts) not a product surface; no live state detection, re-runs blindly; no per-item undo history | MIT, ideas + attribution OK, no code lift needed |
| **memstechtips/Winhance** | Excellent integrated WPF customization UX; good grouping of Windows features; "remove vs disable" distinction | **Licence has a no-compete/redistribution restriction** then behaviour & UX study only, reimplement from MS docs | Restricted, **inspiration only** |
| **ChrisTitusTech/winutil** | Huge reach; one-file; tweak+install combined; community-sourced tweak list | Mixes well-sourced and folklore tweaks; little evidence per item; aggressive defaults in places | MIT |
| **farag2/Sophia-Script / SophiApp** | Gold standard for *documented, reversible* functions; each function has a clear on/off; huge coverage; version-gated | Sheer surface area; SophiApp rewrite churn; some items niche | MIT |
| **hellzerg/optimizer** | Single portable exe; broad; per-Windows-version pages | Some tweaks folklore-tier; disables telemetry/serviced items with thin justification | GPL-3.0, **no code reuse** |
| **builtbybel/Bloatynosy / BloatyNosy** | Playful "AI" framing; app-inventory approach | Direction changes; thin safety model | MIT |
| **Greedeks/GTweak** | Modern WPF; exposes deep controls | **Exposes Defender/UAC/SmartScreen off as ordinary toggles**, exactly the framing we reject | GPL-3.0 |
| **thedogecraft/sparkle** | All-in-one tweak+clean+debloat with modern UI ambitions | Broad scope, young; verify tweak provenance | check at integration time |

## Monitoring / inspection

| Tool | Strongest concepts | Notes |
|---|---|---|
| **Windows Task Manager** | The scenario set (find frozen app, what's slow, kill, startup); Win8 heat map; efficiency mode; per-process GPU | Baseline UX to beat; column set is our "simple default" |
| **System Informer** (Process Hacker lineage) | Depth: handles, threads w/ stacks, tokens, memory regions, services per process,.NET perf; kernel driver for privileged reads | Our "expert drill-down" target. We stay user-mode by default; capability-detect the rest |
| **Sysinternals Process Explorer** | Process tree; DLL/handle lower pane; verify signatures; colour legend; "find window's process" | Tree + lower-pane pattern then process inspector |
| **Autoruns** | Exhaustive autostart location coverage; per-entry publisher + signature; "hide Microsoft entries" | Model for Startup center; we curate the safe subset first |
| **TCPView** | Endpoint list, per-process, state, rate; whois on demand only | Model for Network activity; remote lookups opt-in |
| **RAMMap / VMMap** | Physical page categorisation; per-process address space breakdown | Later Memory drill-down |
| **Resource Monitor / PerfMon** | Filtered-by-process cross-resource view; counter catalogue | "Resource timeline" feature idea |
| **HWiNFO** | Sensor coverage + reliability labelling | Sensor provider design: label reliability, never invent |
| **TMOG / Task Manager OG** | Makes telemetry *exciting*: per-core viz, energy, thermals, dense overview, visual modes | Take the energy, keep restraint and readability |

## PowerToys
Large native utility suite; per-module settings app; command palette (PowerToys Run / the new Command Palette); consistent WinUI shell; GitHub-native release/signing. Model for our shell, palette, and release hygiene.

## Cross-cutting lessons
- **Detect live state** before showing a toggle, most script tools don't.
- **Evidence per performance tweak** or it doesn't ship.
- **"Disable X" is not optimization** when X is a security boundary.
- **Undo history** is a rare feature and a real differentiator.
- **One curated, verified catalog** beats a 500-key grab bag.
