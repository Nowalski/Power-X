using Spectre.Console;

namespace PowerX.Cli;

internal static class Help
{
    public static void Print()
    {
        AnsiConsole.Write(new FigletText("PowerX").Color(Color.Teal));
        AnsiConsole.MarkupLine("[grey]Windows Control Center CLI[/]\n");
        var grid = new Grid();
        grid.AddColumn(new GridColumn().PadRight(4));
        grid.AddColumn();
        void Row(string a, string b) => grid.AddRow($"[teal]{a}[/]", b);
        Row("powerx status", "System overview + live CPU / memory sample");
        Row("powerx scan", "Scan tweak state and surface recommendations");
        Row("powerx process list", "List processes  [--sort cpu|mem|io|name] [--top N] [--tree]");
        Row("powerx tweak list", "All tweaks with current state  [--category X] [--recommended]");
        Row("powerx tweak show <id>", "Full explanation of one tweak");
        Row("powerx tweak apply <id>", "Apply a tweak  [--dry-run]");
        Row("powerx tweak revert <id>", "Revert a tweak to the Windows default  [--dry-run]");
        Row("powerx profile list", "Built-in profiles (Recommended, Privacy, Potato mode, …)");
        Row("powerx profile show <id>", "What a profile changes and current state");
        Row("powerx profile apply <id>", "Apply a profile  [--dry-run] [--restore-point]");
        Row("powerx clean", "Scan and size the disk-cleanup categories");
        Row("powerx repair list", "List the repair / diagnostic jobs");
        Row("powerx repair run <#>", "Run one repair job, streaming its output");
        Row("powerx memtest", "User-space RAM test  [--gb N] [--passes N]");
        Row("powerx crashes", "Recent crashes / hangs / bugchecks  [--since 7d] [--dumps] [show <id>]");
        Row("powerx update", "Check the public repo for a newer version");
        Row("powerx history", "Change history timeline  [--revertable]");
        AnsiConsole.Write(grid);
    }
}
