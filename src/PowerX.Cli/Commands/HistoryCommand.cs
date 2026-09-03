using PowerX.Core.Transactions;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class HistoryCommand
{
    public static int Run(string[] args)
    {
        var log = new ChangeLog();
        var records = args.Contains("--revertable") ? log.RevertableChanges() : log.ReadAll();

        if (records.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]No changes recorded yet.[/]");
            return 0;
        }

        foreach (var group in records.OrderByDescending(r => r.Timestamp)
                     .GroupBy(r => r.Timestamp.LocalDateTime.ToString("yyyy-MM-dd")))
        {
            AnsiConsole.MarkupLine($"\n[teal]{group.Key}[/]");
            foreach (var r in group)
            {
                string icon = r.Success
                    ? (r.PreviousState == r.ResultingState ? "[grey]:[/]" : "[green]+[/]")
                    : "[red]x[/]";
                string change = r.PreviousState == r.ResultingState
                    ? $"[grey]{Markup.Escape(r.ResultingState)} (no change)[/]"
                    : $"{Markup.Escape(r.PreviousState)} [grey]->[/] {Markup.Escape(r.ResultingState)}";
                string note = string.IsNullOrWhiteSpace(r.Message) ? "" : $"  [grey]{Markup.Escape(r.Message!)}[/]";
                AnsiConsole.MarkupLine(
                    $"  {icon} {r.Timestamp.LocalDateTime:HH:mm}  {r.Action} [grey]{Markup.Escape(r.TweakId)}[/]  {change}{note}");
            }
        }
        return 0;
    }
}
