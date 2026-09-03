using PowerX.Core.Diagnostics;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class RepairCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] == "list")
        {
            var table = new Table().Border(TableBorder.Rounded);
            table.AddColumn("#");
            table.AddColumn("Category");
            table.AddColumn("Job");
            int i = 1;
            foreach (var j in CommandRunner.Jobs)
                table.AddRow((i++).ToString(), j.Category, Markup.Escape(j.Title) + (j.Destructive ? " [yellow](changes state)[/]" : ""));
            AnsiConsole.Write(table);
            AnsiConsole.MarkupLine("[grey]Run one with:[/] powerx repair run <#>");
            return 0;
        }

        if (args[0] == "run" && args.Length > 1 && int.TryParse(args[1], out int n)
            && n >= 1 && n <= CommandRunner.Jobs.Count)
        {
            var job = CommandRunner.Jobs[n - 1];
            if (job.Destructive && !AnsiConsole.Confirm($"'{job.Title}' changes system state. Continue?", false))
                return 1;

            AnsiConsole.MarkupLineInterpolated($"[teal]{job.Title}[/]\n");
            int code = CommandRunner.RunAsync(job, line => AnsiConsole.WriteLine(line)).GetAwaiter().GetResult();
            AnsiConsole.MarkupLine(code == 0 ? "\n[green]✔ completed[/]" : $"\n[red]✖ exit {code}[/]");
            if (code == 0 && job.OpenReportPath is not null && File.Exists(job.OpenReportPath))
                AnsiConsole.MarkupLineInterpolated($"[grey]Report:[/] {job.OpenReportPath}");
            return code == 0 ? 0 : 1;
        }

        AnsiConsole.MarkupLine("usage: powerx repair [[list]] | run <#>");
        return 1;
    }
}
