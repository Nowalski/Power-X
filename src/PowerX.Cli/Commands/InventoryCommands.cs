using PowerX.Core.Diagnostics;
using PowerX.Core.Startup;
using PowerX.Core.Transactions;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class DriversCommand
{
    public static int Run(string[] args)
    {
        bool oldOnly = args.Contains("--old");
        var all = DriverInventory.Read();
        var shown = oldOnly ? all.Where(d => d.Age is DriverAge.Old or DriverAge.VeryOld).ToList() : all.ToList();

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Device");
        table.AddColumn("Vendor");
        table.AddColumn("Version");
        table.AddColumn("Date");
        table.AddColumn("Flag");
        foreach (var d in shown.Take(oldOnly ? 200 : 80))
        {
            string flag = d.Age switch
            {
                DriverAge.VeryOld => $"[red]{d.AgeYears}y old[/]",
                DriverAge.Old => $"[yellow]{d.AgeYears}y old[/]",
                _ => d.Signed ? "" : "[red]unsigned[/]",
            };
            table.AddRow(Markup.Escape(Trunc(d.Device, 44)), Markup.Escape(Trunc(d.Provider, 22)),
                Markup.Escape(d.Version), d.Date?.ToString("yyyy-MM") ?? "?", flag);
        }
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLine($"[grey]{all.Count} drivers, {all.Count(d => d.Age is DriverAge.Old or DriverAge.VeryOld)} three years old or more. PowerX never installs a driver.[/]");
        return 0;
    }

    private static string Trunc(string s, int n) => s.Length <= n ? s : s[..(n - 1)] + "…";
}

internal static class TasksCommand
{
    public static int Run(string[] args)
    {
        var all = TaskInventory.Enumerate();
        string? only = args.Contains("--telemetry") ? "telemetry"
            : args.Contains("--reviewed") ? "reviewed" : null;

        var shown = only switch
        {
            "telemetry" => all.Where(t => t.Stance == TaskStance.Telemetry).ToList(),
            "reviewed" => all.Where(t => t.Stance != TaskStance.Unreviewed).ToList(),
            _ => all.ToList(),
        };

        foreach (var g in shown.GroupBy(t => t.Stance).OrderBy(g => g.Key))
        {
            AnsiConsole.MarkupLine($"\n[teal]{g.Key}[/]");
            foreach (var t in g.OrderBy(t => t.Path, StringComparer.OrdinalIgnoreCase))
            {
                string state = t.Enabled ? "[green]on [/]" : "[grey]off[/]";
                AnsiConsole.MarkupLine($"  {state} {Markup.Escape(t.Path)}");
                if (!string.IsNullOrWhiteSpace(t.StanceNote))
                    AnsiConsole.MarkupLine($"      [grey]{Markup.Escape(t.StanceNote)}[/]");
            }
        }
        AnsiConsole.MarkupLine($"\n[grey]{all.Count} tasks. Disable one from the Scheduled tasks page (reversible).[/]");
        return 0;
    }
}

internal static class FirewallCommand
{
    public static int Run(string[] args)
    {
        var state = FirewallRules.ProfileState();
        AnsiConsole.MarkupLine($"[teal]Firewall[/]  domain {On(state.DomainOn)}  private {On(state.PrivateOn)}  public {On(state.PublicOn)}");

        var rules = FirewallRules.Rules();
        var review = rules.Where(r => r.WorthReviewing).ToList();
        if (review.Count > 0)
        {
            AnsiConsole.MarkupLine($"\n[yellow]{review.Count} broad inbound-allow rule{(review.Count == 1 ? "" : "s")} worth a look:[/]");
            foreach (var r in review)
                AnsiConsole.MarkupLine($"  [grey]-[/] {Markup.Escape(r.Name)}  ({Markup.Escape(r.Protocol)} {Markup.Escape(r.LocalPorts)})");
        }

        if (!args.Contains("--all"))
        {
            AnsiConsole.MarkupLine($"\n[grey]{rules.Count} rules total. Add --all to list them, or use the Firewall page.[/]");
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Dir"); table.AddColumn("Action"); table.AddColumn("Name"); table.AddColumn("Program");
        foreach (var r in rules.Take(400))
            table.AddRow(r.Direction == FwDirection.In ? "in" : "out",
                r.Action == FwAction.Allow ? "allow" : "block",
                Markup.Escape(r.Name.Length > 40 ? r.Name[..39] + "…" : r.Name),
                Markup.Escape(string.IsNullOrEmpty(r.Program) ? "-" : System.IO.Path.GetFileName(r.Program)));
        AnsiConsole.Write(table);
        return 0;
    }

    private static string On(bool b) => b ? "[green]on[/]" : "[red]off[/]";
}

internal static class EventsCommand
{
    public static int Run(string[] args)
    {
        var window = args.Contains("--30d") ? TimeSpan.FromDays(30)
            : args.Contains("--24h") ? TimeSpan.FromHours(24) : TimeSpan.FromDays(7);
        bool warnings = args.Contains("--warnings");

        var groups = EventLogBrowser.Read(window, warnings);
        if (groups.Count == 0)
        {
            AnsiConsole.MarkupLine("[green]Nothing logged in this window.[/]");
            return 0;
        }
        foreach (var g in groups.Take(40))
        {
            string lvl = g.Level switch
            {
                EventLevel2.Critical => "[red]CRIT[/]",
                EventLevel2.Error => "[red]ERR [/]",
                _ => "[yellow]WARN[/]",
            };
            AnsiConsole.MarkupLine($"{lvl} [grey]x{g.Count,-4}[/] {Markup.Escape(g.Provider)} [grey]id {g.EventId} ({g.Log})[/]");
            string note = g.Explanation ?? g.SampleMessage;
            if (!string.IsNullOrWhiteSpace(note))
                AnsiConsole.MarkupLine($"      [grey]{Markup.Escape(note.Length > 160 ? note[..159] + "…" : note)}[/]");
        }
        return 0;
    }
}

internal static class ConfigCommand
{
    public static int Run(string[] args)
    {
        if (args.Length >= 1 && args[0] is "export")
        {
            string path = args.ElementAtOrDefault(1) ?? $"powerx-setup-{DateTime.Now:yyyy-MM-dd}.json";
            var bundle = ConfigBundleService.Export();
            File.WriteAllText(path, ConfigBundleService.ToJson(bundle));
            AnsiConsole.MarkupLine($"[green]Exported[/] {bundle.AppliedTweaks.Count} tweak{(bundle.AppliedTweaks.Count == 1 ? "" : "s")} to {Markup.Escape(path)}");
            return 0;
        }
        if (args.Length >= 2 && args[0] is "import")
        {
            if (!File.Exists(args[1])) { AnsiConsole.MarkupLine("[red]File not found.[/]"); return 1; }
            var bundle = ConfigBundleService.FromJson(File.ReadAllText(args[1]));
            if (bundle is null) { AnsiConsole.MarkupLine("[red]Not a PowerX setup file.[/]"); return 1; }

            var plan = ConfigBundleService.Plan(bundle, []);
            AnsiConsole.MarkupLine($"[teal]Would apply[/] {plan.TweaksToApply.Count} tweak{(plan.TweaksToApply.Count == 1 ? "" : "s")}; {plan.TweaksAlreadyApplied.Count} already applied.");
            foreach (var t in plan.TweaksToApply) AnsiConsole.MarkupLine($"  [green]+[/] {Markup.Escape(t.Label)}");
            foreach (var w in plan.Warnings) AnsiConsole.MarkupLine($"  [yellow]{Markup.Escape(w)}[/]");

            if (!args.Contains("--apply"))
            {
                AnsiConsole.MarkupLine("[grey]Add --apply to apply the tweaks.[/]");
                return 0;
            }
            var result = ConfigBundleService.ApplyTweaks(plan);
            AnsiConsole.MarkupLine($"[green]{result.Succeeded} applied[/], {result.AlreadyConfigured} already set, {result.Failed} failed.");
            return result.Failed > 0 ? 1 : 0;
        }

        AnsiConsole.MarkupLine("[grey]Usage:[/] powerx config export [path]  |  powerx config import <path> [--apply]");
        return 1;
    }
}
