# Crash & dump diagnostics, design investigation

Status: **v1 implemented** (D-022). `PowerX.Core/Diagnostics/Crash/*`, `powerx crashes`, and the
"Crash insights" page ship the WER + event-log + bugcheck-catalog + bounded-minidump path
described here. `Win32_ReliabilityRecords` and non-elevated dump fault-attribution remain
fast-follows. This document is kept as the rationale and the safety contract.

The guiding rule: **PowerX must never pretend to diagnose a crash when the evidence is
insufficient.** Every report separates *observed facts*, *likely causes*, *confidence*,
*possible remediation*, and *what is still missing*.

---

## 1. What Windows already records (no dump parsing needed)

### 1.1 Windows Error Reporting (WER), the report store

WER writes a per-event folder under one of:

| Path | Scope | Notes |
|---|---|---|
| `%LOCALAPPDATA%\Microsoft\Windows\WER\ReportArchive\` | current user, submitted | one folder per event |
| `%LOCALAPPDATA%\Microsoft\Windows\WER\ReportQueue\` | current user, not yet submitted | |
| `%ProgramData%\Microsoft\Windows\WER\ReportArchive\` | machine / services / SYSTEM | needs elevation to read |
| `%ProgramData%\Microsoft\Windows\WER\ReportQueue\` | machine | |
| `%ProgramData%\Microsoft\Windows\WER\Temp\` | transient, being written | skip, may be locked |

Each folder holds a `Report.wer` (INI-style, UTF-16) plus optional `.mdmp`, `WERInternalMetadata.xml`,
and CSVs. `Report.wer` alone gives, with **no symbols and no dump**:

- `EventType` (`APPCRASH`, `AppHangB1`, `BEX` / `BEX64` = buffer-overrun/DEP, `MoAppCrash`,...)
- Faulting **application** name, version, timestamp hash (`P1`-`P3`)
- Faulting **module** name, version, timestamp hash (`P4`-`P6`)
- **Exception code** (`P7`, e.g. `c0000005` = access violation, `c000027b` = stowed WinRT exception,
 `e0434352` = managed exception, `80000003` = breakpoint)
- **Fault offset** within the module (`P8`), an RVA, useful only with symbols
- `Sig[n].Name`/`Value` pairs, `DynamicSig`, OS version, `TargetAppId`, `AppPath`
- Attached file list (tells us whether a minidump exists and where)

This is the highest-value, lowest-risk source. Reading the current user's `ReportArchive` needs
no elevation.

### 1.2 Event log. Application channel

- **`Application Error`** (Event ID **1000**): faulting app + module + exception code + fault
 offset + process id + faulting module path. Mirror of WER `P1`-`P8`, but timestamped and
 correlatable.
- **`Application Hang`** (ID **1002**): hung app, "hang type", cross-process ghosting.
- **`Windows Error Reporting`** (ID **1001**): the bucket id, response, and **the path to the
 `.mdmp`** WER kept.
- **`.NET Runtime`** (ID **1026**): the **full managed exception type + message + stack trace**
 for unhandled CLR exceptions. For a.NET app this is often a complete diagnosis on its own,
 no dump required.

Read via `System.Diagnostics.Eventing.Reader.EventLogReader` with an XPath query scoped to
these providers and IDs. The Application log is readable by normal users.

### 1.3 Kernel bugchecks (BSOD). System channel + memory.dmp

- **`Microsoft-Windows-WER-SystemErrorReporting`** (ID **1001**) in the **System** log: the
 bugcheck line, `0x0000001E (0x..., 0x..., 0x..., 0x...)` and "a dump was saved in `C:\Windows\MEMORY.DMP`".
- **`EventLog`** (ID **6008**): "unexpected shutdown" with the time.
- Dump files:
 - `%SystemRoot%\MEMORY.DMP`, kernel/complete/automatic dump (large; **needs elevation**)
 - `%SystemRoot%\Minidump\*.dmp`, 64-512 KB kernel minidumps, one per bugcheck (**needs elevation**)
- The bugcheck **code** and its four parameters are the diagnosis anchor. A static table maps
 the code to a name and a plain-language meaning (e.g. `0x133` = `DPC_WATCHDOG_VIOLATION`,
 `0x1E` = `KMODE_EXCEPTION_NOT_HANDLED`, `0xEF` = `CRITICAL_PROCESS_DIED`, `0x50` =
 `PAGE_FAULT_IN_NONPAGED_AREA`). Parameter 1 of `0x50`/`0x0A`/`0xD1` is a faulting address;
 for many codes parameter reveals the offending driver name string.
- **`LiveKernelReports`** (`%SystemRoot%\LiveKernelReports\*.dmp`), collected without a full
 crash (e.g. a hung GPU: `WATCHDOG`, `NDIS`, `PoW32kWatchdog`). Directory name = category.

### 1.4 Reliability history

`Win32_ReliabilityRecords` WMI class (source: `Microsoft-Windows-Reliability`) is the same data
behind `perfmon /rel`, a pre-correlated timeline of app crashes, hangs, driver failures,
Windows-update failures and "unexpected shutdowns" with a `ProductName`, `Message` and
`TimeGenerated`. Cheap, normal-user readable, and a good "what happened recently" summary
without touching a single dump.

---

## 2. What a minidump adds, and what it costs

### 2.1 User-mode `.mdmp`

A parser can, **without symbols**, read the minidump stream directory and extract:

- `SystemInfoStream`. OS build, processor arch, CPU count
- `MiscInfoStream`, process create/kernel/user times
- `ModuleListStream`, every loaded module: base, size, **version**, **timestamp**, name, and
 the **CodeView PDB record** (pdb name + GUID + age, the key needed to *later* fetch symbols
 if the user opts in)
- `ThreadListStream` / `ExceptionStream`, the faulting thread id, the exception record
 (code, address, parameters), and the **raw stack memory + register context** of each thread
- `Memory64ListStream` / `MemoryListStream`, the captured stack bytes

**Without symbols** you can already say: "the faulting instruction is at `nvwgf2umx.dll+0x4a1c3`,
version 31.0.15.3623, and that module is on the faulting thread's stack" then *likely an NVIDIA
display-driver fault*. You **cannot** name the function, give accurate line numbers, or walk a
frame-pointer-omitted stack reliably.

**With symbols** (PDBs) you get real function names and a proper stack walk. PowerX will **not**
download symbols automatically (privacy + the Microsoft Symbol Server EULA + bandwidth). A
future opt-in could point at a user-provided `_NT_SYMBOL_PATH`.

### 2.2 Kernel `.dmp`

Meaningfully parsing a kernel minidump requires the debugger engine (`dbghelp.dll` +
`dbgeng.dll`, or DbgX) and symbols. That is a large dependency and a security surface
(`dbgeng` loads and executes symbol-server plugins). **Out of scope.** For BSODs, PowerX
stays at the event-log + bugcheck-table level, which is where ~80% of the actionable signal
is anyway.

### 2.3 Do NOT

- Do **not** load `.dmp` contents into a debugger engine that can execute extensions.
- Do **not** download symbols, SOS, or any binary as part of a scan.
- Do **not** upload dumps anywhere. WER already asked the user about that.
- Do **not** parse dumps found via a user-supplied arbitrary path without the same
 size/section sanity checks as any file parser (see).

---

## 3. Privacy

Dump paths and support reports are sensitive:

- `Report.wer` `AppPath` and the minidump `ModuleListStream` leak the **full paths** of the
 user's installed software (and sometimes usernames in those paths).
- A minidump's stack memory can contain **secrets**, passwords, tokens, file contents that
 were on the stack at crash time. PowerX must never display raw stack bytes, never copy a
 dump anywhere, and treat any "share this report" affordance as an explicit, reviewed export
 (redact `\Users\<name>\` then `\Users\<user>\`, drop environment blocks, drop the memory
 streams).
- The support-bundle feature (`powerx report`, roadmap) must let the user see exactly what
 goes in and must default to *not* including dumps.

---

## 4. Performance & permissions

| Operation | Cost | Elevation |
|---|---|---|
| Enumerate user `WER\ReportArchive` + parse `Report.wer` | ms per report, tens of reports | none |
| Event-log XPath query (IDs 1000/1001/1002/1026, last 30 d) | 10-100 ms | none |
| `Win32_ReliabilityRecords` | 100-300 ms (WMI) | none |
| Enumerate `C:\Windows\Minidump` | ms | **yes** (folder ACL) |
| Parse a user-mode `.mdmp` header + streams (no memory) | 5-50 ms for a 1-5 MB dump | depends on dump location |
| Parse `MEMORY.DMP` | avoid (can be GBs) | yes |

All of it runs off the UI thread and is cancellable. Nothing here needs a background service -
it is a pull, on demand, from a Diagnostics page or a CLI command.

---

## 5. Safe parsing boundaries

A dump/`.wer` parser is a file parser fed attacker-influenceable input (a crafted `.mdmp` in a
folder). Rules:

- Read-only, `FileShare.ReadWrite`, never memory-map the whole file.
- Validate `MINIDUMP_HEADER.Signature == 'PMDM'` and `Version` low word `== 0xA793`.
- Every `MINIDUMP_DIRECTORY` RVA + size must lie within the file length; reject on overflow.
- Cap: stream count <= 128, module count <= 4096, thread count <= 4096, any string <= 32 KB.
- Never allocate based on an unvalidated count/size field.
- No pointer following into "memory" streams for anything user-visible.
- Wrap the whole parse; a malformed dump yields `ParseResult.Unreadable(reason)`, never a throw
 across the Core boundary.

### Test fixtures

- A tiny hand-built valid `.mdmp` (header + `SystemInfoStream` + `ModuleListStream` +
 `ExceptionStream`, no memory) checked into `tests/fixtures/` as bytes.
- Truncated / bad-signature / RVA-past-EOF / count-overflow variants.
- Real `Report.wer` samples (INI text) for the common `EventType`s, with paths scrubbed.
- A captured Event 1000 / 1026 XML sample.
- The bugcheck-code table gets a test that every entry has a name + a non-empty explanation and
 that codes are unique.

---

## 6. Proposed architecture

```
PowerX.Core/Diagnostics/Crash/
 CrashScanner.cs, orchestrates the sources below, returns CrashInsight[]
 WerReportReader.cs, parse Report.wer (INI, UTF-16), classify EventType
 EventLogCrashReader.cs. EventLogReader XPath for 1000/1001/1002/1026 + System WER 1001
 ReliabilityReader.cs. Win32_ReliabilityRecords
 MinidumpReader.cs, header + directory + SystemInfo/Module/Thread/Exception streams only
 BugcheckCatalog.cs, static code -> (name, meaning, common causes)
 CrashInsight.cs, the record below
```

```csharp
public sealed record CrashInsight
{
 public required DateTimeOffset When { get; init; }
 public required CrashKind Kind { get; init; } // AppCrash, AppHang, ManagedException, Bugcheck, LiveKernelReport
 public required string Subject { get; init; } // "PowerX.exe 0.1.0" or "KMODE_EXCEPTION_NOT_HANDLED (0x1E)"

 // --- observed facts (only what the source literally stated) ---
 public IReadOnlyList<string> Facts { get; init; } = [];

 // --- interpretation ---
 public IReadOnlyList<string> LikelyCauses { get; init; } = [];
 public Confidence Confidence { get; init; } // High / Moderate / Low / Insufficient
 public IReadOnlyList<string> Remediation { get; init; } = [];
 public IReadOnlyList<string> Missing { get; init; } = []; // "symbols for nvwgf2umx.dll", "the minidump was not kept"

 public string? DumpPath { get; init; } // for "open folder", never auto-read further
 public string SourceRef { get; init; } = ""; // "WER ReportArchive\\AppCrash_...", "Event 1000 @..."
}
```

Confidence rubric:

| Confidence | When |
|---|---|
| **High** | A managed exception with a full stack (Event 1026), or a bugcheck whose parameters name a third-party driver, or an `APPCRASH` whose faulting module is a well-known third-party component clearly on the stack. |
| **Moderate** | Faulting module identified but it is a system DLL that is commonly just the *messenger* (`ntdll`, `KERNELBASE`, `combase`, `Windows.UI.Xaml`); exception code is specific. |
| **Low** | Only `EventType` + exception code, faulting module is generic, no dump. |
| **Insufficient** | WER recorded the event but kept nothing usable; or the dump is unreadable. PowerX says so and stops. It does **not** guess. |

### CLI

```
powerx crashes # recent CrashInsight list (user WER + event log + reliability)
powerx crashes --since 7d
powerx crashes show <id> # full facts / causes / confidence / missing for one
powerx crashes --dumps # also scan C:\Windows\Minidump (requires elevation; says so if not)
```

### WinUI

A **Repair > Crash insights** card (or a small dedicated page): a timeline of `CrashInsight`
rows, each expandable to the four labelled sections, with "Open the report folder" and "Copy
details" (details = the scrubbed text, never the dump). No "fix it" button, remediation is
advice (update this driver, run `sfc`, check RAM with the existing memory test).

---

## 7. Open questions for the decision record

1. Is a user-mode minidump parser worth the maintenance vs. staying purely at the
 WER/event-log level for v1? (Leaning: **event-log + WER + reliability for v1**, minidump
 `ModuleList`/`Exception` streams as a fast follow, kernel dumps never.)
2. Where does it live in the UI, a Repair card, or its own nav entry under "Optimize & fix"?
3. `powerx report` support bundle: crash insights are a natural section, coordinate the
 redaction rules so there is one implementation.
4. Bugcheck catalog: hand-curated (~60 common codes) vs. generated from the Windows docs. Hand
 curat first; each entry cites its source.
