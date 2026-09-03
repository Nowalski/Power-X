using PowerX.Core.Diagnostics;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class MemTestCommand
{
    public static int Run(string[] args)
    {
        long gb = 2;
        int passes = 2;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--gb") long.TryParse(args[i + 1], out gb);
            if (args[i] == "--passes") int.TryParse(args[i + 1], out passes);
        }

        long bytes = gb * 1024 * 1024 * 1024;
        long safe = MemoryTest.SafeMaxBytes();
        if (bytes > safe)
        {
            bytes = safe;
            AnsiConsole.MarkupLineInterpolated($"[yellow]Capped to {Format.Bytes((ulong)safe)} of free memory.[/]");
        }

        using var cts = new CancellationTokenSource();
        System.Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        MemoryTestResult? result = null;
        AnsiConsole.Progress()
            .Columns(new TaskDescriptionColumn(), new ProgressBarColumn(), new PercentageColumn(), new SpinnerColumn())
            .Start(ctx =>
            {
                var task = ctx.AddTask("Memory test");
                var progress = new Progress<MemoryTestProgress>(p =>
                {
                    task.Value = p.Percent;
                    task.Description = p.Pass == 0
                        ? $"[grey]{p.Phase}[/]"
                        : $"Pass {p.Pass}/{p.TotalPasses} · {p.Phase} · {p.MegabytesPerSecond / 1024:0.0} GB/s";
                });
                result = MemoryTest.Run(bytes, passes, progress, cts.Token);
                task.Value = 100;
            });

        if (result is null) return 1;
        AnsiConsole.WriteLine();
        if (result.Passed)
            AnsiConsole.MarkupLineInterpolated($"[green]✔ No errors.[/] Tested {Format.Bytes((ulong)result.BytesTested)} · {result.Passes} passes · {result.Elapsed:mm\\:ss} · {result.AverageMBps / 1024:0.0} GB/s");
        else
            AnsiConsole.MarkupLineInterpolated($"[red]✖ {result.Errors.Count} error(s).[/] This memory (or its overclock) is unstable. First at offset 0x{result.Errors[0].ByteOffset:X}.");
        return result.Passed ? 0 : 1;
    }
}
