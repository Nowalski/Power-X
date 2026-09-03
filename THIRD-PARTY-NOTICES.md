# Third-party notices

PowerX is MIT-licensed. It uses the following third-party packages. This file will be
regenerated from the restore graph in CI; the list below is maintained by hand until then.

| Package | Licence | Use |
|---|---|---|
| Spectre.Console | MIT | CLI rendering |
| YamlDotNet | MIT | configuration import/export (planned) |
| Microsoft.Extensions.Logging.Abstractions | MIT | logging abstraction in Core |
| Microsoft.WindowsAppSDK | MIT (+ MS SDK licence for redistributables) | WinUI 3 desktop app |
| Microsoft.Windows.SDK.BuildTools | MS SDK licence | WinUI build tooling |
| xunit, xunit.runner.visualstudio | Apache-2.0 / MIT | tests |
| FluentAssertions | Apache-2.0 (6.x) | test assertions |
| NSubstitute | BSD-3-Clause | test doubles (planned) |
| Microsoft.NET.Test.Sdk | MIT | test host |

No source code is copied from any third-party project. Reference utilities (Win11Debloat,
Winhance, Sophia Script, System Informer, Sysinternals, PowerToys, TMOG, …) were studied for
behaviour and UX only; all implementation is written against Microsoft documentation and the
public NT API surface. See `docs/research/LICENSE_REVIEW.md`.

Segoe UI Variable and Segoe Fluent Icons ship with Windows and are used, not redistributed.
