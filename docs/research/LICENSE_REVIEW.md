# Licence review

**Our intended licence:** MIT (pending sign-off, DECISIONS D-002).

**Hard rules**
- **No copying code** from any project below without a licence that permits it *and* an explicit decision recorded here.
- **GPL / copyleft code is never imported** into PowerX (would force relicensing).
- **Restricted / no-compete licences: behaviour and UX study only**, reimplement from primary sources (Microsoft docs, `winternl.h`, Group Policy reference).
- Every reused snippet (if any) gets an entry here + a header comment + `THIRD-PARTY-NOTICES.md`.

## Assessment

| Project | Licence (verify at integration) | May we reuse code? | What we take |
|---|---|---|---|
| Raphire/Win11Debloat | MIT | Yes, w/ notice | Nothing lifted; preset philosophy, per-action explanations, reversibility list as a cross-check for our catalog |
| farag2/Sophia-Script | MIT | Yes, w/ notice | Cross-reference for which settings are safely reversible and their registry locations (we re-derive + cite MS) |
| ChrisTitusTech/winutil | MIT | Yes, w/ notice | Tweak-list cross-reference only; we do not inherit unsourced entries |
| memstechtips/Winhance | **Custom, contains redistribution/no-compete restriction** | **No** | UX/interaction study only. Reimplement from MS docs. |
| Greedeks/GTweak | GPL-3.0 | **No** | Negative example (security toggles framed as optimization) |
| hellzerg/optimizer | GPL-3.0 | **No** | Ideas only |
| builtbybel/* | MIT (varies per repo) | Case by case | App-inventory framing |
| thedogecraft/sparkle | verify | Case by case | Feature-set comparison; verify tweak provenance before any adoption |
| System Informer | MIT (core) / GPL bits historically (Process Hacker), **verify per file** | **Only MIT-clean files, w/ decision here** | Concepts: handle/thread/module inspector layout, per-process services. Re-implement against NT APIs. |
| Sysinternals (Process Explorer, Autoruns, TCPView, RAMMap, VMMap) | Proprietary (MS EULA), **not open source** | **No** | Concepts and the documented APIs behind them |
| Microsoft PowerToys | MIT | Yes, w/ notice | Shell patterns, command-palette UX, release/signing hygiene. WinUI approach. |
| TMOG / Task Manager OG | verify (site, not obviously OSS) | **No** unless a clear OSS licence is found | Visualization *ideas* only |

## Dependencies (NuGet), all permissive
| Package | Licence |
|---|---|
| Spectre.Console | MIT |
| YamlDotNet | MIT |
| xunit, FluentAssertions, NSubstitute | Apache-2.0 / MIT / BSD-3 |
| Microsoft.Extensions.* | MIT |
| (future) Microsoft.WindowsAppSDK | MIT + proprietary redistributables under the MS SDK licence, standard for WinUI apps |

## Fonts / assets
Segoe UI Variable + Segoe Fluent Icons ship with Windows and are licensed for use **on Windows** only, fine for an app that runs on Windows; do not embed/redistribute. Provide a fallback stack. Any custom icons: original work or CC0/MIT sets, tracked here.

## Action items before v0.1 tag
- [x] Confirm MIT with project owner; add `LICENSE`.
- [x] Re-verify Winhance and Sparkle licence text at time of any feature parity work.
- [x] Per-file check of any System Informer concept we implement (we expect to write our own against NT APIs, no lift).
- [x] Generate `THIRD-PARTY-NOTICES.md` from the restore graph in CI.
- Full Permission to use Codes from all MIT of them GTweak only research code and Visual,