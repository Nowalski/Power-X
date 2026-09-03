using PowerX.Core.Diagnostics;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class SecurityCommand
{
    public static int Run(string[] args)
    {
        if (args.Length > 0 && args[0] == "scan")
        {
            bool full = args.Contains("--full");
            AnsiConsole.MarkupLine($"[grey]Launching a {(full ? "full" : "quick")} Microsoft Defender scan. Ctrl+C to stop.[/]");
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
            int code = Defender.RunScanAsync(full, AnsiConsole.WriteLine, cts.Token).GetAwaiter().GetResult();
            return code == 0 ? 0 : 1;
        }

        var s = Defender.Status();
        AnsiConsole.MarkupLine("[teal]Microsoft Defender[/]");
        if (s.Detail is { } d) AnsiConsole.MarkupLineInterpolated($"[yellow]{d}[/]");

        var g = new Grid().AddColumn(new GridColumn().PadRight(3)).AddColumn();
        void Row(string k, string v) => g.AddRow($"[grey]{k}[/]", v);
        Row("Mode", s.ModeText);
        Row("Real-time protection", OnOff(s.RealTimeProtection));
        Row("Cloud protection", OnOff(s.CloudProtection));
        Row("Behavior monitoring", OnOff(s.BehaviorMonitor));
        Row("Tamper protection", OnOff(s.TamperProtection));
        Row("Network protection", OnOff(s.NetworkProtection));
        Row("PUA protection", s.PuaProtection);
        Row("Definitions", string.IsNullOrEmpty(s.SignatureVersion)
            ? "unknown"
            : $"{s.SignatureVersion}  ({s.SignatureAgeDays} day(s) old" + (s.SignatureUpdated is { } su ? $", {su.LocalDateTime:g})" : ")"));
        if (s.LastQuickScan is { } q) Row("Last quick scan", q.LocalDateTime.ToString("g"));
        if (s.LastFullScan is { } f) Row("Last full scan", f.LocalDateTime.ToString("g"));
        if (s.ExclusionCount > 0) Row("Exclusions", $"{s.ExclusionCount} configured");
        AnsiConsole.Write(g);

        if (s.Unprotected)
            AnsiConsole.MarkupLine("\n[red]This machine has no active real-time antivirus. Turn Defender or another antivirus back on.[/]");

        var threats = Defender.ThreatHistory(30);
        AnsiConsole.MarkupLine($"\n[teal]Threat history[/]  [grey]({threats.Count} shown)[/]");
        if (threats.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]Nothing recorded. Defender has not reported any detections.[/]");
        }
        else
        {
            foreach (var t in threats)
            {
                string sev = t.Severity switch { "Severe" or "High" => $"[red]{t.Severity}[/]", "Moderate" => $"[yellow]{t.Severity}[/]", _ => $"[grey]{t.Severity}[/]" };
                string state = t.State == DefenderThreatState.ActionFailed ? "[red]action failed[/]" : $"[grey]{t.State.ToString().ToLowerInvariant()}[/]";
                AnsiConsole.MarkupLine($"  {t.When.LocalDateTime:yyyy-MM-dd HH:mm}  {sev}  {Markup.Escape(t.Name)}  {state}"
                    + (t.Active ? "  [red](still active)[/]" : ""));
                if (t.Resource is { } r) AnsiConsole.MarkupLineInterpolated($"      [grey]{r}[/]");
            }
        }
        AnsiConsole.MarkupLine("\n[grey]PowerX is not an antivirus. This is Defender's own status and history.[/]");
        return 0;
    }

    private static string OnOff(bool b) => b ? "[green]on[/]" : "[red]off[/]";
}
