using PowerX.Core.Diagnostics;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class ReportCommand
{
    public static int Run(string[] args)
    {
        var opt = new ReportOptions { Redact = !args.Contains("--no-redact") };

        string md;
        try { md = SystemReport.BuildMarkdown(opt); }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Could not build the report:[/] {ex.Message}");
            return 1;
        }

        int outIdx = Array.IndexOf(args, "--out");
        string? outPath = outIdx >= 0 && outIdx + 1 < args.Length ? args[outIdx + 1] : null;

        if (args.Contains("--print") && outPath is null)
        {
            Console.Out.Write(md);
            return 0;
        }

        outPath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            $"PowerX-report-{DateTime.Now:yyyy-MM-dd-HHmm}.md");

        try
        {
            File.WriteAllText(outPath, md);
        }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Could not write {outPath}:[/] {ex.Message}");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"[green]Report written:[/] {outPath}");
        if (opt.Redact)
            AnsiConsole.MarkupLine("[grey]User name, machine name and hardware identifiers are redacted. Use --no-redact for the full report.[/]");
        return 0;
    }
}
