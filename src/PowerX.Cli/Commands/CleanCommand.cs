using PowerX.Core.Diagnostics;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class CleanCommand
{
    public static int Run(string[] args)
    {
        var targets = CleanupScanner.BuildTargets().ToList();
        AnsiConsole.Status().Start("Scanning cleanup locations…", _ =>
        {
            foreach (var t in targets) CleanupScanner.Measure(t);
        });

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Category");
        table.AddColumn(new TableColumn("Files").RightAligned());
        table.AddColumn(new TableColumn("Size").RightAligned());
        table.AddColumn("Recommended");
        foreach (var t in targets)
            table.AddRow(t.Name, t.FileCount.ToString("N0"), Format.Bytes((ulong)t.SizeBytes),
                t.RecommendedDefault ? "[green]✓[/]" : "");
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLineInterpolated($"[grey]Total reclaimable:[/] {Format.Bytes((ulong)targets.Sum(t => t.SizeBytes))}");
        AnsiConsole.MarkupLine("[grey]Run the cleanup from the app's Tools page.[/]");
        return 0;
    }
}
