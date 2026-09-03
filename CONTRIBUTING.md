# Contributing

## Build and test
```
dotnet build
dotnet test
dotnet run --project src/PowerX.Cli -- <command>
```
Target framework: `net10.0-windows` (`net10.0-windows10.0.19041.0` for the WinUI app). Analyzers run at `latest-recommended`. Keep the interop surface small and commented. Every registry key and NT constant gets a comment saying what it is.

## Ground rules
- The GUI and CLI contain no system logic. They call `PowerX.Core`. A registry write outside `TweakEngine` is a bug.
- Providers return `ProviderResult<T>` and never throw across the boundary. Render "unavailable", never a fake `0`.
- No `irm | iex`, no downloaded binaries, no security exclusions, no piracy or activation features, no secrets.

## Adding a tweak

A pull request that adds a `TweakDefinition` must have all of:

- [ ] A stable, namespaced `Id` (`area.kebab-name`) that will never change.
- [ ] `WhatItDoes`, `WhyYouMightWant` and `Downside` in plain language, no jargon.
- [ ] A `Risk` class (see below). `SecurityTradeoff` and `Destructive` are never `Recommended` and never enter a default profile.
- [ ] At least one `Evidence` entry: a Microsoft doc, a `winternl.h` reference, a Group Policy reference, or a benchmark. Folklore is rejected. See [`docs/research/TWEAK_RESEARCH.md`](docs/research/TWEAK_RESEARCH.md) for the list of tweaks that have already been turned down.
- [ ] `MinBuild` and `MaxBuild` if the setting is build-gated, with `NotApplicable` verified outside the range.
- [ ] The `Restart` scope set correctly.
- [ ] An idempotent `Operation`: `Apply` twice equals one `Apply`; `Detect` after `Apply` returns `Applied`; after `Revert` it returns `Default`.
- [ ] `DefaultValue` is the value Windows ships with (or `null`, meaning revert deletes the value).
- [ ] A round-trip test in `PowerX.Core.Tests` against a throwaway `HKCU\Software\PowerX.Tests\<guid>` key.
- [ ] `docs/TWEAK_CATALOG.md` regenerated: `dotnet run --project src/PowerX.Cli -- tweak docs > docs/TWEAK_CATALOG.md`.
- [ ] For any performance claim, a before-and-after measurement, or the evidence text states "no measurable benefit on test hardware".

### Risk classes

| Class | Meaning | Confirm | In a default profile |
|---|---|---|---|
| `Low` | Cosmetic or trivially reversible | No | Yes, if `Recommended` |
| `Moderate` | Changes Windows behaviour, low breakage risk | Inline note | Only in a matching profile (Gaming, Privacy) |
| `Advanced` | May affect software compatibility | Summary plus downside | Opt-in list only |
| `SecurityTradeoff` | Reduces a Windows protection (Defender, SmartScreen, UAC, VBS) | Explicit security dialog | Never |
| `Destructive` | Hard or impossible to fully reverse | Names what is lost and how to restore it | Never |

## Adding a metrics provider
- Implement the domain provider returning `ProviderResult<T>`. Capability-detect: missing hardware, API or privilege becomes `Unavailable` with a `Detail`.
- Stateful providers document the cadence they need.
- Add a live-machine invariant test (value ranges, internal consistency, current process present).

## Commit and pull request
- Short imperative subject with an area prefix (`core:`, `cli:`, `tweak:`, `docs:`).
- Update [`docs/DECISIONS.md`](docs/DECISIONS.md) for anything architectural.
- CI must be green: build, analyzers and tests.
