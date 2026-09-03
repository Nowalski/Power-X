using PowerX.Core.Transactions;
using PowerX.Core.Tweaks;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class TweakCommand
{
    private static TweakEngine BuildEngine() => new(TweakCatalog.Default);

    public static int Run(string[] args)
    {
        if (args.Length == 0) { AnsiConsole.MarkupLine("usage: powerx tweak [teal]list|show|apply|revert[/] ..."); return 1; }
        var engine = BuildEngine();
        return args[0] switch
        {
            "list" => List(engine, args),
            "docs" => Docs(engine),
            "show" => Show(engine, args),
            "apply" => Execute(engine, args, ChangeAction.Apply),
            "revert" => Execute(engine, args, ChangeAction.Revert),
            _ => Unknown(args[0]),
        };
    }

    private static int List(TweakEngine engine, string[] args)
    {
        string? category = Arg(args, "--category");
        bool recommendedOnly = args.Contains("--recommended");
        var ctx = TweakContext.Detect();

        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.AddColumn("ID");
        table.AddColumn("Name");
        table.AddColumn("Category");
        table.AddColumn("Risk");
        table.AddColumn("State");
        table.AddColumn("Rec");

        foreach (var s in engine.GetAllStatus(ctx).OrderBy(s => s.Definition.Category).ThenBy(s => s.Definition.Id))
        {
            var d = s.Definition;
            if (category is not null && !d.Category.Equals(category, StringComparison.OrdinalIgnoreCase)) continue;
            if (recommendedOnly && !d.Recommended) continue;
            table.AddRow(
                $"[grey]{d.Id}[/]",
                Markup.Escape(d.Name),
                d.Category,
                RiskMarkup(d.Risk),
                StateMarkup(s.State),
                d.Recommended ? "[green]✓[/]" : "");
        }
        AnsiConsole.Write(table);
        if (!ctx.IsElevated)
            AnsiConsole.MarkupLine("[grey]All current tweaks are per-user (HKCU) and need no elevation.[/]");
        return 0;
    }

    private static int Show(TweakEngine engine, string[] args)
    {
        if (args.Length < 2) { AnsiConsole.MarkupLine("usage: powerx tweak show <id>"); return 1; }
        var status = engine.Find(args[1]) is { } def ? engine.GetStatus(def.Id) : null;
        if (status is null) { AnsiConsole.MarkupLineInterpolated($"[red]no such tweak:[/] {args[1]}"); return 1; }
        var d = status.Definition;

        var g = new Grid();
        g.AddColumn(new GridColumn().PadRight(2));
        g.AddColumn();
        g.AddRow("[grey]ID[/]", d.Id);
        g.AddRow("[grey]Category[/]", d.Category);
        g.AddRow("[grey]Risk[/]", RiskMarkup(d.Risk));
        g.AddRow("[grey]Recommended[/]", d.Recommended ? "[green]yes[/]" : "no");
        g.AddRow("[grey]Restart[/]", d.Restart == RestartScope.None ? "none" : d.Restart.ToString());
        g.AddRow("[grey]State[/]", StateMarkup(status.State));
        g.AddRow("[grey]What it does[/]", Markup.Escape(d.WhatItDoes));
        g.AddRow("[grey]Why[/]", Markup.Escape(d.WhyYouMightWant));
        g.AddRow("[grey]Downside[/]", Markup.Escape(d.Downside));
        if (d.MinBuild > 0 || d.MaxBuild > 0)
            g.AddRow("[grey]Builds[/]", $"{(d.MinBuild > 0 ? d.MinBuild : 0)} to {(d.MaxBuild > 0 ? d.MaxBuild : "latest")}");
        foreach (var e in d.Sources)
            g.AddRow("[grey]Source[/]", Markup.Escape(e.Summary) + (e.Url is null ? "" : $"  [blue]{e.Url}[/]"));

        AnsiConsole.Write(new Panel(g).Header($"[teal]{Markup.Escape(d.Name)}[/]").Border(BoxBorder.Rounded));
        return 0;
    }

    private static int Execute(TweakEngine engine, string[] args, ChangeAction action)
    {
        if (args.Length < 2) { AnsiConsole.MarkupLineInterpolated($"usage: powerx tweak {action.ToString().ToLowerInvariant()} <id> [[--dry-run]]"); return 1; }
        var def = engine.Find(args[1]);
        if (def is null) { AnsiConsole.MarkupLineInterpolated($"[red]no such tweak:[/] {args[1]}"); return 1; }

        bool dryRun = args.Contains("--dry-run");
        var ctx = TweakContext.Detect() with { DryRun = dryRun };

        var rec = engine.Execute(def.Id, action, ctx);
        var verb = action == ChangeAction.Apply ? "Apply" : "Revert";
        string id = Markup.Escape(def.Id);
        if (rec.Success)
        {
            string change = rec.PreviousState == rec.ResultingState
                ? $"[yellow]no change[/] (already {rec.ResultingState})"
                : $"[green]{rec.PreviousState} → {rec.ResultingState}[/]";
            string suffix = dryRun ? "  [grey](dry-run)[/]" : "";
            AnsiConsole.MarkupLine($"{verb} {id}: {change}{suffix}");
            if (def.Restart != RestartScope.None && rec.PreviousState != rec.ResultingState)
                AnsiConsole.MarkupLine($"[yellow]restart required:[/] {def.Restart}");
            return 0;
        }

        AnsiConsole.MarkupLine($"[red]{verb} {id} failed:[/] {Markup.Escape(rec.Message ?? "")}");
        return 1;
    }

    private static int Unknown(string s) { AnsiConsole.MarkupLineInterpolated($"[red]unknown:[/] {s}"); return 1; }

    /// <summary>Emit the tweak reference as Markdown (docs/TWEAK_CATALOG.md is generated from this).</summary>
    private static int Docs(TweakEngine engine)
    {
        var w = Console.Out;
        w.WriteLine("# Tweak catalog\n");
        w.WriteLine("> Generated from `TweakCatalog` by `powerx tweak docs`. Do not edit by hand.\n");
        foreach (var g in engine.Catalog.GroupBy(t => t.Category).OrderBy(g => g.Key))
        {
            w.WriteLine($"## {g.Key}\n");
            foreach (var d in g.OrderBy(t => t.Id))
            {
                w.WriteLine($"### `{d.Id}`: {d.Name}\n");
                w.WriteLine($"- **What it does:** {d.WhatItDoes}");
                w.WriteLine($"- **Why you might want it:** {d.WhyYouMightWant}");
                w.WriteLine($"- **Downside:** {d.Downside}");
                w.WriteLine($"- **Risk:** {d.Risk}{(d.Recommended ? " · **Recommended**" : "")}");
                w.WriteLine($"- **Restart:** {(d.Restart == RestartScope.None ? "none" : d.Restart.ToString())}");
                w.WriteLine($"- **Privilege:** {d.Privilege}");
                string builds = d.MinBuild == 0 && d.MaxBuild == 0
                    ? "all supported builds"
                    : $"{(d.MinBuild == 0 ? "≤" : d.MinBuild + " ≤")} build {(d.MaxBuild == 0 ? "" : "≤ " + d.MaxBuild)}".Trim();
                w.WriteLine($"- **Compatibility:** {builds}");
                foreach (var e in d.Sources)
                    w.WriteLine($"- **Source:** {e.Summary}{(e.Url is null ? "" : $" (<{e.Url}>)")}");
                w.WriteLine();
            }
        }
        return 0;
    }

    private static string RiskMarkup(TweakRisk r) => r switch
    {
        TweakRisk.Low => "[green]Low[/]",
        TweakRisk.Moderate => "[yellow]Moderate[/]",
        TweakRisk.Advanced => "[orange1]Advanced[/]",
        TweakRisk.SecurityTradeoff => "[red]Security trade-off[/]",
        TweakRisk.Destructive => "[red]Destructive[/]",
        _ => r.ToString(),
    };

    private static string StateMarkup(TweakState s) => s switch
    {
        TweakState.Applied => "[green]Applied[/]",
        TweakState.Default => "[grey]Default[/]",
        TweakState.Custom => "[yellow]Custom[/]",
        TweakState.NotApplicable => "[grey]n/a[/]",
        _ => "[grey]?[/]",
    };

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
