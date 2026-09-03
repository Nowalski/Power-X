# Contributing

## Build & test
```
dotnet build
dotnet test
dotnet run --project src/PowerX.Cli -- <command>
```
Target: `net10.0-windows` (`net10.0-windows10.0.19041.0` for the WinUI app). Analyzers on (`latest-recommended`). Keep the interop surface small and commented — **no unexplained registry or NT constants** (prompt/design rule §64).

## Ground rules
- GUI and CLI contain **no system logic** — they call `PowerX.Core`. A registry write outside `TweakEngine` is a bug.
- Providers return `ProviderResult<T>` and never throw across the boundary. Render "unavailable", never a fake `0`.
- No `irm | iex`, no downloaded binaries, no security exclusions, no piracy/activation features, no secrets.

## Adding a tweak — hard checklist
A PR adding a `TweakDefinition` **must** have all of:

- [ ] Stable, namespaced `Id` (`area.kebab-name`) that will never change.
- [ ] `WhatItDoes`, `WhyYouMightWant`, `Downside` — plain language, no jargon.
- [ ] `Risk` per [`docs/RISK_MATRIX.md`](docs/RISK_MATRIX.md). `SecurityTradeoff`/`Destructive` are **never** `Recommended` and never enter a default profile.
- [ ] At least one `Evidence` — a Microsoft doc, `winternl.h`, a Group Policy reference, or a benchmark. Folklore is rejected (see [`docs/research/TWEAK_RESEARCH.md`](docs/research/TWEAK_RESEARCH.md) for the refused list).
- [ ] `MinBuild` / `MaxBuild` if the surface is build-gated. Verified `NotApplicable` outside the range.
- [ ] `Restart` scope set correctly.
- [ ] `Operation` is **idempotent**: `Apply` twice = one `Apply`; `Detect` after `Apply` = `Applied`; after `Revert` = `Default`.
- [ ] `DefaultValue` is the *shipped Windows* value (or `null` ⇒ revert deletes the value).
- [ ] Round-trip test in `PowerX.Core.Tests` using a throwaway `HKCU\Software\PowerX.Tests\<guid>` key.
- [ ] `docs/TWEAK_CATALOG.md` regenerated: `dotnet run --project src/PowerX.Cli -- tweak docs > docs/TWEAK_CATALOG.md`.
- [ ] For any performance claim: a benchmark per [`docs/BENCHMARK_PLAN.md`](docs/BENCHMARK_PLAN.md), or the evidence text states "no measurable benefit on test hardware".

## Adding a metrics provider
- Implement the domain provider returning `ProviderResult<T>`; capability-detect (missing hardware / API / privilege ⇒ `Unavailable` with a `Detail`).
- Stateful providers document their required cadence.
- Add a live-machine invariant test (ranges, consistency, current-process-present style assertions).

## Commit / PR
- Conventional-ish subject (`core:`, `cli:`, `tweak:`, `docs:`), imperative mood.
- Update [`docs/DECISIONS.md`](docs/DECISIONS.md) for anything architectural.
- CI must be green (build + analyzers + tests).
