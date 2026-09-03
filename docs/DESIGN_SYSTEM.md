# Design system

Goal: a premium Windows engineering tool, not a crypto dashboard. Visual quality lives in hierarchy, typography, alignment, density, tables, empty states, transitions and restraint, not in effects bolted on at the end.

## Foundations
- **Base**: Windows 11 Fluent. Native title bar, subtle Mica on the window backdrop, Acrylic only on transient surfaces (flyouts, the command palette, dialogs).
- **Type**: Segoe UI Variable, with the fallback stack `Segoe UI, system-ui, sans-serif`. Tabular figures for every metric value.
- **Icons**: Segoe Fluent Icons. Custom glyphs only where the set has a gap.
- **Accent**: one restrained accent, the system accent by default. Status is carried by shape, label and position first; colour is secondary.
- **Radius**: 8 px on cards and dialogs, 4 px on controls, 0 on table cells and dense list rows. Do not round everything.
- **Elevation**: flat by default. One shadow step, for flyouts and dialogs only.
- **Density**: compact but breathable. Table rows 28 to 32 px, 4 px internal rhythm, 12 to 16 px page gutters.
- **Motion**: 90 to 150 ms, Fluent easing. Purpose only: navigation, expand and collapse, data arriving, selection, the palette. No long marketing animations. Respect "reduced motion".

## Colour tokens (both themes defined explicitly)

| Token | Light | Dark | Use |
|---|---|---|---|
| `bg.window` | Mica | Mica | window |
| `bg.layer` | #FBFBFB | #202020 | content cards |
| `bg.row.alt` | #00000005 | #FFFFFF08 | zebra striping |
| `text.primary` | #1A1A1A | #F3F3F3 | |
| `text.secondary` | #5C5C5C | #A0A0A0 | labels, units |
| `stroke.divider` | #00000010 | #FFFFFF14 | separators |
| `accent` | system | system | selection, focus, active nav |

## Heat map
Resource cells get a background wash whose alpha rises with intensity; the numeric value is always shown.
- Below 20 percent: none. 20 to 50: heat 1. 50 to 80: heat 2. 80 to 95: heat 3. Above 95: heat 4.
- Hues: CPU blue, memory violet, disk amber, network teal, GPU green. All pass a 3:1 contrast ratio in both themes and in high contrast, where the wash is replaced by a left border rule.
- The CLI mirrors this: grey, then yellow, then orange, then red, alongside the value.

## Charts
- Y axis: fixed 0 to 100 for percentages, auto-scaled for bytes and rates with the unit in the axis label.
- History windows: 30 s, 60 s, 5 min, longer where the ring buffer allows. Older data is discarded.
- Sampling once a second; the render interpolates to 60 fps. Both back off when the window is hidden.
- Sparklines in tables share the row's heat hue. No axis, no labels, hover for the value.

## States
- **Empty**: one line saying what would appear and the action to make it appear. Never a blank panel.
- **Loading**: skeleton rows matching the final layout for lists; an inline spinner of at most one line for actions.
- **Unavailable**: "Temperature data unavailable on this system", never a fake `0` and never a greyed-out fake gauge.
- **Error**: a plain sentence plus a "view technical details" disclosure holding the HRESULT or log reference.

## Accessibility
A full keyboard path for every action. Visible focus (a 2 px accent ring). Screen-reader names on all controls and chart summaries ("CPU 14 percent, trending up"). The high-contrast theme is honoured. No colour-only status. Text contrast at least 4.5:1, UI contrast at least 3:1.
