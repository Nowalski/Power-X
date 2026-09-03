using PowerX.Core.Diagnostics;
using PowerX.Core.Transactions;
using PowerX.Core.Tweaks;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class ProfileCommand
{
    private static TweakEngine BuildEngine() => new(TweakCatalog.Default);

    public static int Run(string[] args)
    {
        if (args.Length == 0) { AnsiConsole.MarkupLine("usage: powerx profile [teal]list|show|apply[/] ..."); return 1; }
        return args[0] switch
        {
            "list" => List(),
            "show" => Show(args),
            "apply" => Apply(args),
            _ => Unknown(args[0]),
        };
    }

    private static int List()
    {
        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.AddColumn("ID");
        table.AddColumn("Name");
        table.AddColumn("Tone");
        table.AddColumn("Tweaks");
        table.AddColumn("Description");
        foreach (var p in Profiles.All)
            table.AddRow(
                $"[grey]{p.Id}[/]",
                Markup.Escape(p.Name),
                p.Tone.ToString(),
                p.Tone == ProfileTone.Restore ? "—" : p.TweakIds.Count.ToString(),
                Markup.Escape(p.Description));
        AnsiConsole.Write(table);
        return 0;
    }

    private static int Show(string[] args)
    {
        if (args.Length < 2) { AnsiConsole.MarkupLine("usage: powerx profile show <id>"); return 1; }
        var p = Profiles.Get(args[1]);
        if (p is null) { AnsiConsole.MarkupLineInterpolated($"[red]no such profile:[/] {args[1]}"); return 1; }
        var engine = BuildEngine();

        AnsiConsole.Write(new Panel(Markup.Escape(p.Description)).Header($"[teal]{Markup.Escape(p.Name)}[/]").Border(BoxBorder.Rounded));
        if (p.Tone == ProfileTone.Restore)
        {
            AnsiConsole.MarkupLine("[grey]Reverts every tweak PowerX has applied.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Tweak");
        table.AddColumn("State");
        foreach (var id in p.TweakIds)
        {
            var s = engine.GetStatus(id);
            table.AddRow(Markup.Escape(s.Definition.Name), s.State.ToString());
        }
        AnsiConsole.Write(table);
        return 0;
    }

    private static int Apply(string[] args)
    {
        if (args.Length < 2) { AnsiConsole.MarkupLine("usage: powerx profile apply <id> [[--dry-run]] [[--restore-point]]"); return 1; }
        var p = Profiles.Get(args[1]);
        if (p is null) { AnsiConsole.MarkupLineInterpolated($"[red]no such profile:[/] {args[1]}"); return 1; }

        bool dryRun = args.Contains("--dry-run");
        bool restore = p.Tone == ProfileTone.Restore;
        var ctx = TweakContext.Detect() with { DryRun = dryRun };
        var engine = BuildEngine();
        var action = restore ? ChangeAction.Revert : ChangeAction.Apply;

        IReadOnlyList<string> ids = restore
            ? engine.GetAllStatus(ctx).Where(s => s.State == TweakState.Applied).Select(s => s.Definition.Id).ToList()
            : p.TweakIds;

        if (ids.Count == 0)
        {
            AnsiConsole.MarkupLine(restore ? "[grey]Nothing to restore.[/]" : "[grey]Profile has no tweaks.[/]");
            return 0;
        }

        if (!dryRun && args.Contains("--restore-point"))
        {
            var rp = SystemRestore.Create($"PowerX before {p.Name}");
            AnsiConsole.MarkupLine(rp.Success
                ? "[green]restore point created[/]"
                : $"[yellow]restore point skipped:[/] {Markup.Escape(rp.Message ?? "")}");
        }

        var result = engine.ApplyMany(ids, action, ctx);
        foreach (var r in result.Records)
        {
            var change = r.PreviousState == r.ResultingState
                ? $"[yellow]no change[/] ({r.ResultingState})"
                : $"[green]{r.PreviousState} → {r.ResultingState}[/]";
            var mark = r.Success ? change : $"[red]failed:[/] {Markup.Escape(r.Message ?? "")}";
            AnsiConsole.MarkupLine($"  {Markup.Escape(r.TweakName)}: {mark}");
        }

        AnsiConsole.MarkupLine($"\n[teal]{result.Succeeded} changed[/], {result.AlreadyConfigured} already set, " +
                               (result.Failed > 0 ? $"[red]{result.Failed} failed[/]" : "0 failed") +
                               (dryRun ? "  [grey](dry-run)[/]" : ""));
        if (result.Restart.Any)
        {
            var scopes = new List<string>();
            if (result.Restart.Explorer) scopes.Add("Explorer restart");
            if (result.Restart.SignOut) scopes.Add("sign out");
            if (result.Restart.Reboot) scopes.Add("reboot");
            if (result.Restart.Application) scopes.Add("PowerX restart");
            AnsiConsole.MarkupLine($"[yellow]to take full effect:[/] {string.Join(", ", scopes)}");
        }
        return result.Failed > 0 ? 1 : 0;
    }

    private static int Unknown(string s) { AnsiConsole.MarkupLineInterpolated($"[red]unknown:[/] {s}"); return 1; }
}
