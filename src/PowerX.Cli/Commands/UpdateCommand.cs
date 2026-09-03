using PowerX.Core.Diagnostics;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class UpdateCommand
{
    public static int Run(string[] args)
    {
        var current = typeof(UpdateCommand).Assembly.GetName().Version ?? new Version(0, 1, 0);
        AnsiConsole.MarkupLine($"[grey]Current version:[/] {current.ToString(3)}");

        var result = AnsiConsole.Status().Start("Checking for updates...",
            _ => UpdateChecker.CheckAsync(current).GetAwaiter().GetResult());

        if (result.Error is not null)
        {
            AnsiConsole.MarkupLine($"[red]Check failed:[/] {Markup.Escape(result.Error)}");
            return 1;
        }

        if (!result.UpdateAvailable)
        {
            AnsiConsole.MarkupLine("[green]You're on the latest version.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[green]Update available:[/] {result.Latest}");
        if (result.Notes is not null) AnsiConsole.MarkupLine($"[grey]{Markup.Escape(result.Notes)}[/]");
        if (result.DownloadUrl is not null) AnsiConsole.MarkupLine($"[blue]{Markup.Escape(result.DownloadUrl)}[/]");

        if (args.Contains("--download"))
        {
            if (!result.HasVerifiedInstaller)
            {
                AnsiConsole.MarkupLine("[yellow]This release has no hash-pinned installer to download; open the releases page above.[/]");
                return 0;
            }

            DownloadResult dl = default!;
            AnsiConsole.Progress().Start(ctx =>
            {
                var task = ctx.AddTask("Downloading + verifying the installer");
                var progress = new Progress<double>(p => task.Value = p * 100);
                dl = UpdateInstaller.DownloadVerifiedAsync(result, progress).GetAwaiter().GetResult();
                task.Value = 100;
            });

            if (dl.Ok && dl.Path is not null)
            {
                AnsiConsole.MarkupLine($"[green]Verified.[/] {Markup.Escape(dl.Path)}");
                AnsiConsole.MarkupLine("[grey]Run it to upgrade:[/] msiexec /i \"" + dl.Path + "\"");
                return 0;
            }

            AnsiConsole.MarkupLine($"[red]Download failed:[/] {Markup.Escape(dl.Error ?? "unknown error")}");
            return 1;
        }

        AnsiConsole.MarkupLine("[grey]Add[/] --download [grey]to fetch and verify the installer.[/]");
        return 0;
    }
}
