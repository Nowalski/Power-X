# Design system

Goal: **premium Windows engineering tool**, not a crypto dashboard. Visual quality lives in hierarchy, typography, alignment, density, tables, empty states, transitions and restraint — not effects bolted on at the end (prompt §29–33, §75).

## Foundations
- **Base:** Windows 11 Fluent — native title bar, subtle **Mica** on the window backdrop, **Acrylic** only on transient surfaces (flyouts, command palette, dialogs).
- **Type:** Segoe UI Variable (Display / Text / Small optical sizes), fallback `Segoe UI, system-ui, sans-serif`. Tabular figures for all metric values.
- **Icons:** Segoe Fluent Icons; custom glyphs only where the set has a gap (original/CC0).
- **Accent:** one restrained accent (system accent by default). Status is communicated by **shape + label + position**, colour is secondary.
- **Radius:** Windows geometry — 8 px cards/dialogs, 4 px controls, 0 for table cells and dense list rows. Do not round everything.
- **Elevation:** flat by default; 1 shadow step for flyouts/dialogs only.
- **Density:** compact but breathable. Table row 28–32 px; 4 px internal rhythm; 12–16 px page gutters.
- **Motion:** 90–150 ms, Fluent easing. Purpose only: navigation, expand/collapse, data-in, selection, palette. No 600 ms marketing animations. Respect "reduced motion".

## Colour tokens (both themes defined explicitly)
| Token | Light | Dark | Use |
|---|---|---|---|
| `bg.window` | Mica | Mica | window |
| `bg.layer` | #FBFBFB | #202020 | content cards |
| `bg.row.alt` | #00000005 | #FFFFFF08 | zebra |
| `text.primary` | #1A1A1A | #F3F3F3 | |
| `text.secondary` | #5C5C5C | #A0A0A0 | labels, units |
| `stroke.divider` | #00000010 | #FFFFFF14 | separators |
| `accent` | system | system | selection, focus, active nav |
| `heat.1..4` | see below | | resource intensity |

## Heat map (prompt §33)
Resource cells get a background wash whose **alpha** rises with intensity; the numeric value is always present.
- `<20%` none · `20–50%` `heat.1` · `50–80%` `heat.2` · `80–95%` `heat.3` · `>95%` `heat.4`.
- Hues: CPU cool-blue, memory violet, disk amber, network teal, GPU green. All pass 3:1 contrast on text in both themes and in high-contrast (where the wash is replaced by a left border rule).
- CLI mirror: `Format.Heat` (grey → yellow → orange → red + the value).

## Charts (prompt §57)
- Y axis: fixed 0–100 for %, auto-nice for bytes/rates with the unit in the axis label.
- History windows: 30 s / 60 s / 5 min / (longer where the ring buffer allows). Data older than the largest window is discarded.
- Sampling 1 s; render interpolates to 60 fps; both drop when hidden.
- Sparklines in tables share the row's heat hue; no axis, no labels, hover for the value.

## States
- **Empty:** one line of what would appear + the action to make it appear. Never a blank panel.
- **Loading:** skeleton rows matching final layout for lists; inline spinner ≤ 1 line for actions.
- **Unavailable:** "Temperature data unavailable on this system" — never `0°C`, never a greyed fake gauge.
- **Error:** plain sentence + "View technical details" disclosure holding the HRESULT/log ref.

## Accessibility (prompt §52)
Full keyboard path for every action; visible focus (2 px accent ring); screen-reader names on all controls and chart summaries ("CPU 14 percent, trending up"); high-contrast theme honoured; no colour-only status; min 4.5:1 text / 3:1 UI.

## Two audiences (prompt §34)
Default view answers "what's happening / what's slow". Every surface has a deeper level (columns, inspector tabs, expert toggles) reachable without leaving the page.
