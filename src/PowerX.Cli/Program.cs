using PowerX.Cli;
using PowerX.Cli.Commands;
using Spectre.Console;

// Box-drawing and other glyphs Spectre emits are UTF-8; the default Windows console
// codepage mangles them. Best-effort — harmless if the console rejects it.
try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch (Exception) { /* redirected / unsupported */ }

if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
{
    Help.Print();
    return 0;
}

try
{
    var rest = args.Skip(1).ToArray();
    return args[0].ToLowerInvariant() switch
    {
        "status" => StatusCommand.Run(rest),
        "scan" => ScanCommand.Run(rest),
        "process" or "proc" => ProcessCommand.Run(rest),
        "tweak" => TweakCommand.Run(rest),
        "profile" => ProfileCommand.Run(rest),
        "clean" or "cleanup" => CleanCommand.Run(rest),
        "repair" => RepairCommand.Run(rest),
        "memtest" => MemTestCommand.Run(rest),
        "crashes" or "crash" => CrashCommand.Run(rest),
        "security" or "defender" => SecurityCommand.Run(rest),
        "hash" => HashCommand.Run(rest),
        "report" => ReportCommand.Run(rest),
        "update" => UpdateCommand.Run(rest),
        "history" => HistoryCommand.Run(rest),
        "changes" or "changed" => ChangesCommand.Run(rest),
        "reboot" or "pending-reboot" => RebootCommand.Run(rest),
        "battery" => BatteryCommand.Run(rest),
        "storage" or "du" => StorageCommand.Run(rest),
        "version" or "--version" => Version(),
        _ => Unknown(args[0]),
    };
}
catch (Exception ex)
{
    AnsiConsole.MarkupLineInterpolated($"[red]error:[/] {ex.Message}");
    return 1;
}

static int Version()
{
    AnsiConsole.WriteLine($"PowerX CLI {typeof(Program).Assembly.GetName().Version}");
    return 0;
}

static int Unknown(string cmd)
{
    AnsiConsole.MarkupLineInterpolated($"[red]unknown command:[/] {cmd}");
    Help.Print();
    return 1;
}

internal sealed partial class Program;
