# Third-party notices

PowerX is MIT-licensed. It uses the following third-party packages. The list is maintained by hand;
update it whenever `Directory.Packages.props` changes.

| Package | Licence | Use |
|---|---|---|
| Spectre.Console | MIT | CLI rendering |
| Microsoft.Extensions.Logging.Abstractions | MIT | logging abstraction in Core |
| Microsoft.Diagnostics.Tracing.TraceEvent | MIT | real-time ETW session for per-process network usage |
| Microsoft.Management.Infrastructure | MIT | CIM sessions (network adapter enable / disable) |
| System.Management | MIT | WMI queries (Defender, System Restore, hardware) |
| System.Diagnostics.EventLog | MIT | event-log reading |
| System.ServiceProcess.ServiceController | MIT | Windows services view |
| Microsoft.WindowsAppSDK | MIT (+ MS SDK licence for redistributables) | WinUI 3 desktop app |
| Microsoft.Windows.SDK.BuildTools | MS SDK licence | WinUI build tooling |
| Microsoft Visual C++ 2015-2022 runtime | Microsoft Visual Studio redistributable licence | bundled into the published app and the MSI so it starts on a clean Windows install |
| BenchmarkDotNet | MIT | benchmarks (`bench/`, not shipped) |
| xunit, xunit.runner.visualstudio | Apache-2.0 | tests (not shipped) |
| FluentAssertions | Apache-2.0 (6.x) | test assertions (not shipped; stays on 6.x, 8+ changed licence) |
| Microsoft.NET.Test.Sdk | MIT | test host (not shipped) |

No source code is copied from any third-party project. Reference utilities (Win11Debloat,
Winhance, Sophia Script, System Informer, Sysinternals, PowerToys, TMOG, …) were studied for
behaviour and UX only; all implementation is written against Microsoft documentation and the
public NT API surface. See `docs/research/LICENSE_REVIEW.md`.

Segoe UI Variable and Segoe Fluent Icons ship with Windows and are used, not redistributed.
