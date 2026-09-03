<h1 align="center">PowerX</h1>
<p align="center"><em>A native Windows control center. Monitor, configure, debloat, clean, repair and understand Windows from one app.</em></p>

<p align="center">
  <a href="https://nowalski.github.io/Power-X/">Website</a> &nbsp;&nbsp;
  <a href="https://github.com/Nowalski/Power-X/releases/latest">Download</a> &nbsp;&nbsp;
  <a href="docs/PRODUCT_SPEC.md">Product spec</a> &nbsp;&nbsp;
  <a href="docs/DECISIONS.md">Decisions</a>
</p>

---

> **Status: 0.1.0, the first milestone build.**
> Working today on real Windows data: `PowerX.Core` (telemetry, a declarative tweak engine, debloat / startup / services inventories, cleanup and repair engines, crash insights, an append-only change history), the `powerx` CLI, and a WinUI 3 desktop app with live dashboards (CPU, memory, GPU, network, processes), a tweak catalogue with one-click **profiles**, an app-debloat page, a Tools workbench and a diagnostics / repair runner.

PowerX is **not another debloater**. It is a Windows utility that treats deep system monitoring, *safe* Windows configuration, and premium Windows-native design as equally important. It stays useful even if you never remove a single app.

## Download

Grab the latest build from the [releases page](https://github.com/Nowalski/Power-X/releases/latest).

| | |
|---|---|
| **Installer** | `PowerX-Setup-<version>-win-x64.msi`. Per-machine install to Program Files, one Start-menu entry, no desktop shortcut, clean uninstall, in-place upgrade. |
| **Portable** | `PowerX-<version>-portable-win-x64.zip`. Unpack and run `PowerX.App.exe`. Nothing is written outside the folder except a small change history and settings under `%LOCALAPPDATA%\PowerX`. |

Requirements: 64-bit Windows 10 build 19041 or Windows 11. PowerX runs as administrator because it manages system state. It is **not code-signed yet**, so SmartScreen shows an "unknown publisher" prompt: choose *More info*, then *Run anyway*. Each release lists a SHA-256 for every file so you can verify what you downloaded.

## What it does

| Area | |
|---|---|
| **Monitor** | Real-time CPU (total, per-core, kernel time), memory (physical, commit, pools, cache), GPU (engine and VRAM via PDH), network throughput and connections, processes (single-syscall enumeration, per-process CPU, I/O, memory, handles, threads, a real tree), disks (SMART, temperature, endurance). |
| **Configure** | A **declarative, evidence-backed tweak engine** (35 tweaks). Every tweak states what it does, why you might want it, the downside, restart needs, whether it is reversible, and whether it is recommended. Curated **profiles** (Recommended, Privacy, Potato mode, Gaming, Restore defaults) apply a visible set in one click with a preview diff. No folklore gamer tweaks ([why](docs/research/TWEAK_RESEARCH.md)). |
| **Debloat** | About ninety curated entries, Store and consumer apps only, no shell components, nothing pre-selected. Each entry states its removal class and how hard it is to reinstall. |
| **Clean and repair** | Size-first disk cleanup with a per-category breakdown, and a runner for SFC, DISM, chkdsk, network reset and Windows Update repair with streamed output. |
| **Crash insights** | Reads what Windows already recorded (WER, event logs, and only on request the metadata inside a crash dump) and separates observed facts from likely cause, with a confidence level. Never downloads symbols, never opens a dump in a debugger, never uploads anything. |
| **Safety** | Detect, record, plan, show, apply, **verify**, log, undo. An append-only change history. Per-tweak revert. Honest about what cannot be undone. |

## Try the CLI

```
git clone https://github.com/Nowalski/Power-X && cd Power-X
dotnet build
dotnet run --project src/PowerX.Cli -- status
dotnet run --project src/PowerX.Cli -- process list --sort cpu --top 20
dotnet run --project src/PowerX.Cli -- scan
dotnet run --project src/PowerX.Cli -- tweak list
dotnet run --project src/PowerX.Cli -- tweak show privacy.advertising-id
dotnet run --project src/PowerX.Cli -- profile apply lowspec --dry-run
dotnet run --project src/PowerX.Cli -- crashes --since 7d
dotnet run --project src/PowerX.Cli -- history
```

The CLI and the GUI call the **same** `PowerX.Core`. Tweak logic is never duplicated.

## Build

- 64-bit Windows 10 build 19041 or Windows 11
- .NET SDK 10.0.400 or newer (`global.json` pins it; `rollForward: latestFeature`)
- `dotnet build` and `dotnet test` for Core and CLI, no Visual Studio required
- The WinUI app: `dotnet build src/PowerX.App/PowerX.App.csproj -p:Platform=x64` (no workload, no Visual Studio)

### Installer

The MSI is built from [`installer/PowerX.wxs`](installer/PowerX.wxs) with WiX 5:

```
dotnet tool install --global wix --version 5.0.2
wix extension add -g WixToolset.UI.wixext/5.0.2
pwsh installer/build.ps1        # -> publish/PowerX-Setup-<version>-win-x64.msi
```

The app itself is unpackaged and unchanged. The MSI only lays down the self-contained publish folder, with the Visual C++ runtime bundled so it starts on a clean Windows install.

## Repository

```
src/PowerX.Core    telemetry, tweak engine, profiles, debloat, cleanup, repair, transactions, diagnostics   (no UI deps)
src/PowerX.Cli     powerx.exe
src/PowerX.App     WinUI 3 desktop app (unpackaged, x64), built separately from PowerX.sln
installer/         WiX 5 source for the MSI
site/              the homepage published to GitHub Pages
bench/             BenchmarkDotNet harness for telemetry hot paths
tests/             xUnit + FluentAssertions
docs/              spec, architecture, design system, research, plans, audit, decisions
```

## Principles

- **Tell the truth.** Real telemetry or "unavailable"; no fabricated zeros; no unverified FPS claims.
- **Least *unnecessary* software**, not the lowest process count.
- **Progressive disclosure.** Simple by default, deep on demand. Simple is not crippled.
- **Weakening a security boundary is never "optimization"** and never in a default profile.
- **The tool must not be the resource hog.**

## Updates

PowerX checks a small `version.json` in this repo once a day (opt-out in Settings). When a release
has a hash-pinned installer it can download and run it after you confirm; otherwise it just points
you at the releases page. It only ever fetches from this repo's own GitHub releases over HTTPS and
verifies the size and SHA-256 before running anything.

## Licence

**MIT.** See [`LICENSE`](LICENSE). No GPL or restricted code is imported; referenced projects are studied for behaviour and reimplemented from Microsoft documentation. Third-party components are listed in [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md).

## Contributing

See [`CONTRIBUTING.md`](CONTRIBUTING.md), especially *Adding a tweak*, which has a hard checklist: evidence, build range, revert, test, docs.

<p align="center"><sub>Not affiliated with Microsoft. Windows is a trademark of Microsoft Corporation.</sub></p>
