using PowerX.Core.Diagnostics;
using PowerX.Core.Processes;
using PowerX.Core.Tweaks;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class ScanCommand
{
    public static int Run(string[] args)
    {
        var info = SystemInfoProvider.Collect();
        AnsiConsole.MarkupLineInterpolated($"[teal]Understanding your PC…[/]  {info.WindowsEdition} build {info.BuildString}\n");

        var engine = new TweakEngine(TweakCatalog.Default);
        var statuses = engine.GetAllStatus();

        int applied = statuses.Count(s => s.State == TweakState.Applied);
        int recExposed = statuses.Count(s => s.Definition.Recommended && s.State == TweakState.Default);

        var proc = new ProcessProvider();
        var snap = proc.Enumerate();

        var findings = new List<string>();
        foreach (var s in statuses.Where(s => s.Definition.Recommended && s.State == TweakState.Default))
            findings.Add($"[yellow]›[/] {s.Definition.Name}  [grey]({s.Definition.Id})[/]");

        var panel = new Grid();
        panel.AddColumn(new GridColumn().PadRight(2));
        panel.AddColumn();
        panel.AddRow("Processes running", snap.TotalProcesses.ToString());
        panel.AddRow("Tweaks applied", applied.ToString());
        panel.AddRow("Recommended, not yet applied", recExposed.ToString());
        AnsiConsole.Write(new Panel(panel).Header("[teal]Overview[/]").Border(BoxBorder.Rounded));

        if (findings.Count > 0)
        {
            AnsiConsole.MarkupLine("\n[teal]Recommendations[/] [grey](review, nothing is applied automatically)[/]");
            foreach (var f in findings) AnsiConsole.MarkupLine("  " + f);
            AnsiConsole.MarkupLine("\n[grey]Apply one with:[/] powerx tweak apply <id>");
        }
        else
        {
            AnsiConsole.MarkupLine("\n[green]No recommended changes outstanding.[/]");
        }
        return 0;
    }
}
