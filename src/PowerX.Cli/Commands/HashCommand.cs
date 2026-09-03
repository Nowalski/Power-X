using PowerX.Core.Diagnostics;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class HashCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            AnsiConsole.MarkupLine("usage: powerx hash <file path | sha256>");
            return 1;
        }

        string arg = args[0];
        string sha256;

        if (arg.Length == 64 && arg.All(Uri.IsHexDigit))
        {
            sha256 = arg.ToLowerInvariant();
        }
        else if (File.Exists(arg))
        {
            try { sha256 = HashLookup.Sha256FileAsync(arg).GetAwaiter().GetResult(); }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLineInterpolated($"[red]Could not read {arg}:[/] {ex.Message}");
                return 1;
            }
            AnsiConsole.MarkupLineInterpolated($"[grey]SHA-256[/]  {sha256}");
        }
        else
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Not a file or a SHA-256 hash:[/] {arg}");
            return 1;
        }

        var r = HashLookup.CheckAsync(sha256).GetAwaiter().GetResult();
        if (r.Error is { } e && !r.Found)
            AnsiConsole.MarkupLineInterpolated($"[yellow]{r.Summary}[/] ({e})");
        else if (r.KnownMalicious)
            AnsiConsole.MarkupLineInterpolated($"[red]{r.Summary}[/]");
        else
            AnsiConsole.MarkupLineInterpolated($"[grey]CIRCL hashlookup:[/] {r.Summary}");

        AnsiConsole.MarkupLine("[grey]Only the hash was sent, over HTTPS, to hashlookup.circl.lu. PowerX is not an antivirus.[/]");
        return 0;
    }
}
