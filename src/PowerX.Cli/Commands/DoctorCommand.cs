using PowerX.Core.Diagnostics;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class DoctorCommand
{
    public static int Run(string[] args)
    {
        bool deep = args.Contains("--deep");
        AnsiConsole.MarkupLine(deep
            ? "[grey]Scanning (including the component-store analysis, this can take a minute)...[/]"
            : "[grey]Scanning...[/]");

        var report = HealthCheck.ScanAsync(deep).GetAwaiter().GetResult();

        AnsiConsole.MarkupLine($"\n[teal]Score[/] {report.Score}/100   "
            + $"[red]{report.High} high[/]  [yellow]{report.Medium} medium[/]  [grey]{report.Low} low[/]");

        if (report.Items.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Nothing outstanding in any of PowerX's checks.[/]");
            return 0;
        }

        foreach (var group in report.Items.GroupBy(i => i.Impact).OrderBy(g => g.Key))
        {
            string label = group.Key switch
            {
                RecommendationImpact.High => "[red]Worth doing soon[/]",
                RecommendationImpact.Medium => "[yellow]Worth a look[/]",
                _ => "[grey]Minor[/]",
            };
            AnsiConsole.MarkupLine($"\n{label}");
            foreach (var r in group)
            {
                AnsiConsole.MarkupLine($"  - {Markup.Escape(r.Title)}");
                AnsiConsole.MarkupLine($"    [grey]{Markup.Escape(r.Detail)}[/]");
            }
        }
        return 0;
    }
}
