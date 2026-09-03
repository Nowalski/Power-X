# Tweak research, evidence, and what we refuse to ship

## Bar for inclusion
A tweak ships only with: (1) a source / mechanism, (2) plain-language explanation, (3) compatibility scope (build range), (4) a benchmark where feasible, (5) a working revert. If measurable benefit is negligible, we say so in the `Downside`/evidence text.

## Shipped in M1 (all HKCU, reversible, no elevation)

| ID | Mechanism | Evidence | Risk |
|---|---|---|---|
| `explorer.show-file-extensions` | `Explorer\Advanced\HideFileExt=0` | Shell docs; anti-spoofing safety | Low, **Recommended** |
| `explorer.show-hidden-files` | `Advanced\Hidden=1` (def 2) | Shell docs | Moderate |
| `explorer.launch-to-this-pc` | `Advanced\LaunchTo=1` (def 2) | Shell docs | Low |
| `privacy.advertising-id` | `AdvertisingInfo\Enabled=0` | MS "Manage connections..." doc, "Advertising ID" section | Low, **Recommended** |
| `start.disable-recommendations` | `Advanced\Start_IrisRecommendations=0` | Observable; matches Settings toggle; build >= 22621 | Low, **Recommended** |
| `taskbar.hide-widgets` | `Advanced\TaskbarDa=0`; build >= 22000 | Observable; matches Settings toggle | Low |
| `gaming.disable-game-dvr` | `GameConfigStore\GameDVR_Enabled=0` + `CurrentVersion\GameDVR\AppCaptureEnabled=0` | Background capture has real but **small (~1-3% avg, title-dependent)** cost, not the large gains commonly claimed | Moderate |

## Researched, queued (needs broker / more validation)
- Privacy: `DiagTrack` telemetry **level** via `HKLM\...\DataCollection\AllowTelemetry` (Enterprise-only for full "Security"/0 on Pro/Home, must gate by edition), "Tailored experiences" (`Privacy\TailoredExperiencesWithDiagnosticDataEnabled`), activity feed, `Start_TrackDocs`, location consent per Settings.
- Explorer: classic context menu (Win11), remove "Home"/Gallery nav nodes, `SeparateProcess`, disable thumbnail-on-network.
- Search: disable Bing/web results in Start search (`Explorer\DisableSearchBoxSuggestions` / `SearchSettings\IsDynamicSearchBoxEnabled`).
- Taskbar: alignment, "end task" on right-click, combine buttons, remove Copilot/Task View/Chat.
- Explorer/OneDrive, Edge first-run & desktop-search integration.
- Power: expose (not force) Ultimate Performance plan; USB selective-suspend toggle.
- Windows Update: active hours, pause, "get latest as soon as available", driver-exclusion via broker (documented Group Policy keys only).

## Refused (folklore / harmful / unsubstantiated),
| Claim | Verdict |
|---|---|
| Disable HPET / `bcdedit /set useplatformclock` / `disabledynamictick` | **No.** Modern Windows manages this; forcing it regresses on many systems. |
| Global timer-resolution / `GlobalTimerResolutionRequests` | **No** as a persistent tweak. It's a per-process concern; system-wide 0.5 ms hurts power/idle. |
| TCP registry "latency fixes" (`TcpAckFrequency`, `TCPNoDelay`, autotuning off, `NetworkThrottlingIndex=ffffffff`) | **No** blanket. `NetworkThrottlingIndex` is the only one with a narrow, documented audio/streaming rationale, expose as Advanced with that caveat only. |
| Disable IPv6 | **No.** Unsupported configuration per Microsoft; breaks features. |
| Blanket "disable these 30 services" | **No.** Per-service, with dependency + rationale, in Services center. |
| RAM cleaners / standby-list purge as routine "optimization" | **No.** Standby list *is* usable cache. Maybe a one-shot diagnostic button, clearly labelled, never scheduled. |
| Delete Prefetch / Superfetch off for "speed" | **No.** Hurts cold-start on HDD, neutral-to-negative on SSD. |
| Disable Defender / SmartScreen / UAC / VBS "for FPS" | **Security trade-off** class only, never Recommended, strong warning, never in a default profile. VBS/HVCI off may be offered for measured gaming loss on specific hardware, with the security cost stated plainly. |
| `Win32PrioritySeparation` magic values | **No** without per-value evidence; default (2 / 0x26) is fine for desktop. |
| Ultimate Performance everywhere / aggressive BCD | **No.** Offer the plan, don't force it. |

## Method for new performance tweaks
1. State the mechanism and the Microsoft doc, winternl.h or Group Policy source.
2. Define the build range and edition constraints.
3. Measure before and after, at least five runs each side, and report the variance.
4. If the difference is within noise then keep it but label "no measurable performance benefit on test hardware; may help niche cases".
5. Write `Detect`/`Apply`/`Revert`/`Verify`, add a round-trip test, list it here and in `TWEAK_CATALOG.md`.
