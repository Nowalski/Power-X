<h1 align="center">PowerX</h1>
<p align="center"><em>A native Windows control center — monitor, diagnose, optimize, configure, clean, repair, understand.</em></p>

---

> **Status: milestone 1 — foundation. Not yet released.**
> Working today on real Windows data: `PowerX.Core` (telemetry, declarative tweak engine, debloat/startup/services inventories, cleanup + repair engines, change history), the `powerx` CLI, and a WinUI 3 desktop app with live dashboards (CPU / memory / GPU / network / processes), a tweak catalog with one-click **profiles**, an app-debloat page, a Tools workbench and a diagnostics/repair runner. See [`docs/PRODUCT_SPEC.md`](docs/PRODUCT_SPEC.md) and [`docs/DECISIONS.md`](docs/DECISIONS.md).

PowerX is **not another "debloater"**. It is a Windows utility that treats deep system monitoring, *safe* Windows configuration, and premium Windows-native design as equally important. It stays useful even if you never remove a single app.

## What it does

| Pillar | |
|---|---|
| **Monitor** | Real-time CPU (total + per-core + kernel time), memory (physical / commit / pools / cache), GPU (engine + VRAM via PDH), network throughput + connections, processes (single-syscall enumeration, per-process CPU / I/O / memory / handles / threads, process tree), disks (SMART, temperature, endurance). |
| **Configure** | A **declarative, evidence-backed tweak engine** (~31 tweaks). Every tweak answers: what it does, why you'd want it, the downside, restart needs, whether it's reversible, and whether it's recommended. Curated **profiles** — Recommended, Privacy, Potato mode (low-spec), Gaming, Restore defaults — apply a visible set in one click with a preview diff. No folklore "gamer tweaks" ([why](docs/research/TWEAK_RESEARCH.md)). |
| **Debloat** | ~90 curated entries, Store/consumer apps only — no shell components, nothing pre-selected. Each entry states its removal class and how hard it is to reinstall. |
| **Startup & tasks** | Every autostart entry in one list with reversible toggles, boot-performance data (last-boot duration, vs your average, per-entry impact from `Microsoft-Windows-Diagnostics-Performance/Operational`), a "delay after sign-in" option for a slow entry, and a **Scheduled tasks** curator with a stance on the well-known telemetry / updater tasks. |
| **Drivers & firewall** | A driver inventory that flags drivers 3y+ old or unsigned (never installs anything), and a **read-only** firewall-rules view that flags a broad inbound-allow hole. |
| **Event log** | Recent Application / System errors grouped by source and id, with a plain-language note for the common ones. |
| **Clean & repair** | Size-first disk cleanup with a per-category bar, a component-store (WinSxS) analysis + safe cleanup, and a runner for SFC / DISM / chkdsk / network reset / Windows-update repair with streamed output. |
| **Storage explorer** | Point it at a drive or folder and it sizes every child recursively (parallel, reparse-points skipped), largest first, drill-down by click. |
| **What changed** | A daily background config snapshot (startup / tasks / services / programs / drivers / tweaks) with an added-removed-changed diff between any two. Local-only JSON. |
| **Share this setup** | Export the tweaks you have applied to a small JSON file, apply them on another PC behind a preview (`powerx config export\|import`). |
| **Per-process network** | On the Network page: which process is using the bandwidth right now, from a private ETW session (elevated). |
| **Safety** | Detect → record → plan → show → apply → **verify** → log → undo. Append-only change history. Per-tweak revert. Honest about what can't be undone. |
| **Understand** | Plain-language explanations, deterministic recommendations, a support-friendly system report, battery health, a pending-restart check, and a Learn section that debunks common myths. |

## Try the CLI

```
git clone https://github.com/Nowalski/PowerX && cd PowerX
dotnet build
dotnet run --project src/PowerX.Cli -- status
dotnet run --project src/PowerX.Cli -- process list --sort cpu --top 20
dotnet run --project src/PowerX.Cli -- scan
dotnet run --project src/PowerX.Cli -- tweak list
dotnet run --project src/PowerX.Cli -- tweak show privacy.advertising-id
dotnet run --project src/PowerX.Cli -- tweak apply explorer.show-file-extensions --dry-run
dotnet run --project src/PowerX.Cli -- profile list
dotnet run --project src/PowerX.Cli -- profile apply lowspec --dry-run
dotnet run --project src/PowerX.Cli -- clean
dotnet run --project src/PowerX.Cli -- repair list
dotnet run --project src/PowerX.Cli -- history
```

The CLI and the GUI call the **same** `PowerX.Core` — tweak logic is never duplicated.

## Build

- Windows 10 1809+ / Windows 11
- .NET SDK 10.0.400+ (`global.json` pins it; `rollForward: latestFeature`)
- `dotnet build` / `dotnet test` — no Visual Studio required for Core + CLI

## Installer

The desktop app ships as a single **`PowerX-Setup-<version>-win-x64.msi`** (per-machine
install, one Start-menu entry, no desktop shortcut, clean uninstall, in-place upgrade). It's
built from [`installer/PowerX.wxs`](installer/PowerX.wxs) with WiX 5:

```
dotnet tool install --global wix --version 5.0.2
wix extension add -g WixToolset.UI.wixext/5.0.2
pwsh installer/build.ps1        # -> publish/PowerX-Setup-<version>-win-x64.msi
```

The app itself is unpackaged and unchanged — the MSI only lays down the self-contained publish
folder (with the Visual C++ runtime bundled so it starts on a clean Windows install). Not
code-signed yet, so SmartScreen shows "unknown publisher" → *More info → Run anyway*.

## Repository

```
src/PowerX.Core     telemetry, tweak engine, profiles, debloat, cleanup, repair, transactions, diagnostics   (no UI deps)
src/PowerX.Cli       powerx.exe
src/PowerX.App       WinUI 3 desktop app (unpackaged, x64) — not in PowerX.sln; built separately
bench/               BenchmarkDotNet harness for telemetry hot paths (not in PowerX.sln)
tests/…              xUnit + FluentAssertions
docs/                spec, architecture, design system, research, plans, audit
docs/research/       competitor matrix, Task Manager history, Windows APIs, tweak evidence, licence review
```

The WinUI app targets `net10.0-windows10.0.19041.0` and builds with `dotnet build src/PowerX.App/PowerX.App.csproj -p:Platform=x64` (no workload, no Visual Studio).

## Principles

- **Tell the truth** — real telemetry or "unavailable"; no fabricated zeros; no unverified FPS claims.
- **Least *unnecessary* software** — not the lowest process count.
- **Progressive disclosure** — simple by default, deep on demand; simple ≠ crippled.
- **Weakening a security boundary is never "optimization"** and never in a default profile.
- **The tool must not be the resource hog.**

## Licence

Intended: **MIT** (pending sign-off — [`docs/research/LICENSE_REVIEW.md`](docs/research/LICENSE_REVIEW.md)). No GPL or restricted code is imported; referenced projects are studied for behaviour and reimplemented from Microsoft documentation.

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md) — especially *Adding a tweak*, which has a hard checklist (evidence, build range, revert, test, docs).
