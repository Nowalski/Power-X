# Risk model

Every tweak and destructive action carries exactly one class. Drives colour, confirmation, preselection and profile eligibility.

| Class | Meaning | Confirm? | Preselected in a profile? | Undo |
|---|---|---|---|---|
| **Low** | Cosmetic / trivially reversible (Explorer view options, ad ID) | No | Yes, if `Recommended` | Per-tweak, instant |
| **Moderate** | Changes Windows behaviour, low breakage risk (hide widgets, Game DVR off) | Light — inline "this changes X" | Only in a matching non-default profile (Gaming, Privacy) | Per-tweak |
| **Advanced** | May affect software compatibility or system behaviour (context-menu style, search providers, service startup type) | Yes — summary + downside | Never auto; opt-in list | Per-tweak; may need restart |
| **Security trade-off** | Reduces a Windows security protection (Defender, SmartScreen, UAC, VBS/HVCI) | Yes — explicit security-cost dialog, type-to-confirm for the strongest | **Never.** Not in any default profile. Never labelled "Recommended". | Per-tweak; restart; we re-warn on next scan while active |
| **Destructive** | Hard/impossible to fully reverse (provisioned-app removal, feature-on-demand removal, file deletion) | Yes — names exactly what is lost and the restore method (reinstallable / difficult / not reversible) | Never auto | Depends — stated honestly per item; no blanket "undo" promise |

## Reversibility honesty (prompt §21)
Each removable item is tagged:
- **Reversible toggle** — a setting; flip it back.
- **Reinstallable package** — `winget` / Store / `Add-AppxPackage` from a known source; we show the command.
- **Difficult restoration** — possible but needs an ISO / FoD source / account steps.
- **Destructive** — no supported restore; state it before the user proceeds.

## Restart scope
`None` · `Application` · `Explorer` (offered, never automatic) · `SignOut` · `Reboot`. A transaction aggregates the maximum scope and surfaces one prompt.

## Profile rules
- **Recommended** profile = only `Low` + `Recommended` tweaks.
- No profile contains `Security trade-off` or `Destructive`.
- **Developer** profile explicitly *excludes* anything touching WSL, Hyper-V, virtualization, containers, debugging.
- The full diff is always shown before apply; nothing is hidden in a "script".
