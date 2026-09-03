using PowerX.Core.Diagnostics.Crash;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class CrashCommand
{
    public static int Run(string[] args)
    {
        var window = TimeSpan.FromDays(ParseSince(args) ?? 30);
        bool dumps = args.Contains("--dumps");
        bool machine = args.Contains("--machine") || dumps;

        var opt = new CrashScanner.ScanOptions
        {
            Window = window,
            ReadDumps = dumps,
            IncludeMachineStore = machine,
            Max = 60,
        };

        List<CrashInsight> all;
        try { all = CrashScanner.Scan(opt).ToList(); }
        catch (Exception ex)
        {
            AnsiConsole.MarkupLineInterpolated($"[red]crash scan failed:[/] {ex.Message}");
            return 1;
        }

        string? showId = args.SkipWhile(a => a != "show").Skip(1).FirstOrDefault();
        if (showId is not null)
        {
            var one = all.FirstOrDefault(i => i.Id.Equals(showId, StringComparison.OrdinalIgnoreCase));
            if (one is null) { AnsiConsole.MarkupLine($"[yellow]No crash with id[/] {Markup.Escape(showId)} [yellow]in the last {window.Days} days.[/]"); return 1; }
            PrintDetail(one);
            return 0;
        }

        if (all.Count == 0)
        {
            AnsiConsole.MarkupLineInterpolated($"[green]No crashes, hangs or bugchecks recorded in the last {window.Days} days.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("id"); table.AddColumn("when"); table.AddColumn("what");
        table.AddColumn("confidence"); table.AddColumn("likely cause");
        foreach (var i in all)
            table.AddRow(
                $"[grey]{i.Id}[/]",
                i.When.LocalDateTime.ToString("MM-dd HH:mm"),
                Markup.Escape(Truncate(i.Subject, 34)) + KindTag(i.Kind),
                ConfColor(i.Confidence),
                Markup.Escape(Truncate(i.Culprit ?? i.LikelyCauses.FirstOrDefault() ?? "-", 46)));
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine("[grey]Details:[/] powerx crashes show <id>   ·   [grey]add[/] --dumps [grey](needs admin)[/]  --since 7d");
        return 0;
    }

    private static void PrintDetail(CrashInsight i)
    {
        AnsiConsole.MarkupLine($"\n[teal]{Markup.Escape(i.Subject)}[/]  [grey]{i.Kind} · {i.When.LocalDateTime:F}[/]");
        AnsiConsole.MarkupLine($"[grey]source:[/] {Markup.Escape(i.Source)}   [grey]confidence:[/] {ConfColor(i.Confidence)}");

        Section("Observed facts", i.Facts);
        Section("Likely cause(s)", i.LikelyCauses);
        Section("What you can try", i.Remediation);
        Section("Still missing", i.Missing);

        if (i.ArtifactPath is { } p)
            AnsiConsole.MarkupLine($"\n[grey]Report:[/] {Markup.Escape(p)}");
        AnsiConsole.MarkupLine("[grey]PowerX does not download symbols, open dumps in a debugger, or upload anything.[/]");
    }

    private static void Section(string title, IReadOnlyList<string> lines)
    {
        if (lines.Count == 0) return;
        AnsiConsole.MarkupLine($"\n[bold]{title}[/]");
        foreach (var l in lines)
            AnsiConsole.MarkupLine($"  {(l.StartsWith("  ") ? "" : "· ")}{Markup.Escape(l)}");
    }

    private static string KindTag(CrashKind k) => k switch
    {
        CrashKind.Bugcheck => " [red](BSOD)[/]",
        CrashKind.AppHang => " [yellow](hang)[/]",
        CrashKind.ManagedException => " [grey](.NET)[/]",
        _ => "",
    };

    private static string ConfColor(CrashConfidence c) => c switch
    {
        CrashConfidence.High => "[green]high[/]",
        CrashConfidence.Moderate => "[yellow]moderate[/]",
        CrashConfidence.Low => "[grey]low[/]",
        _ => "[grey]insufficient[/]",
    };

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";

    private static int? ParseSince(string[] args)
    {
        int idx = Array.IndexOf(args, "--since");
        if (idx < 0 || idx + 1 >= args.Length) return null;
        var v = args[idx + 1].Trim().ToLowerInvariant();
        var num = new string(v.TakeWhile(char.IsDigit).ToArray());
        if (!int.TryParse(num, out var days)) return null;
        return v.EndsWith('w') ? days * 7 : v.EndsWith('h') ? Math.Max(1, days / 24) : days;
    }
}
