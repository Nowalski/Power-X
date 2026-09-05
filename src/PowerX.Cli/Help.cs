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
        void Row(string a, string b) => grid.AddRow(new Markup($"[teal]{Markup.Escape(a)}[/]"), new Markup(Markup.Escape(b)));
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
        Row("powerx security", "Microsoft Defender status and threat history  (scan [--full])");
        Row("powerx hash <file>", "SHA-256 of a file, checked against the CIRCL hash database");
        Row("powerx report", "Write a system report for support  [--out PATH] [--no-redact] [--print]");
        Row("powerx changes", "What changed since the last snapshot  [--snapshot to take one now]");
        Row("powerx reboot", "Whether a restart is pending, and why");
        Row("powerx battery", "Battery wear, cycle count and runtime");
        Row("powerx temps", "Every temperature reading Windows exposes (ACPI thermal zones, disk sensors)");
        Row("powerx storage <path>", "Size the folders under a path, largest first");
        Row("powerx drivers", "Driver inventory with an age flag  [--old]");
        Row("powerx tasks", "Scheduled tasks with a curated stance  [--telemetry] [--reviewed]");
        Row("powerx firewall", "Firewall status and broad inbound rules  [--all]");
        Row("powerx events", "Recent event-log errors, grouped and explained  [--24h] [--30d] [--warnings]");
        Row("powerx config export|import", "Save or apply a shareable tweak setup  (import --apply)");
        Row("powerx doctor", "Scan the PC and list what is worth doing, most impactful first  [--deep]");
        Row("powerx update", "Check the public repo for a newer version");
        Row("powerx history", "Change history timeline  [--revertable]");
        AnsiConsole.Write(grid);
    }
}
