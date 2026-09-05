using PowerX.Core.Diagnostics;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class RebootCommand
{
    public static int Run(string[] args)
    {
        var status = PendingReboot.Check();
        if (!status.Pending)
        {
            AnsiConsole.MarkupLine("[green]No restart is pending.[/]");
            return 0;
        }
        AnsiConsole.MarkupLine("[yellow]A restart is pending:[/]");
        foreach (var r in status.Reasons)
            AnsiConsole.MarkupLine($"  [grey]-[/] {Markup.Escape(r)}");
        return 0;
    }
}

internal static class BatteryCommand
{
    public static int Run(string[] args)
    {
        var info = BatteryHealth.ReadAsync().GetAwaiter().GetResult();
        if (!info.HasBattery)
        {
            AnsiConsole.MarkupLine("[grey]No battery on this machine.[/]");
            return 0;
        }

        AnsiConsole.MarkupLine($"[teal]Charge[/]      {info.ChargePercent}%  "
            + (info.OnAcPower ? (info.Charging ? "(charging)" : "(plugged in)") : "(on battery)"));
        if (info.EstimatedRuntime is { } rt)
            AnsiConsole.MarkupLine($"[teal]Estimated[/]  {(int)rt.TotalHours}h {rt.Minutes}m left");
        if (info.DesignCapacityMwh > 0)
        {
            AnsiConsole.MarkupLine($"[teal]Capacity[/]   {info.FullChargeCapacityMwh:N0} mWh of {info.DesignCapacityMwh:N0} mWh design");
            AnsiConsole.MarkupLine($"[teal]Wear[/]       {info.WearPercent}%  ([grey]{info.Health}[/])");
        }
        if (info.CycleCount > 0)
            AnsiConsole.MarkupLine($"[teal]Cycles[/]     {info.CycleCount}");
        if (info.Error is not null)
            AnsiConsole.MarkupLine($"[grey]{Markup.Escape(info.Error)}[/]");
        return 0;
    }
}

internal static class TempsCommand
{
    public static int Run(string[] args)
    {
        var report = ThermalInfo.Read();
        if (report.Readings.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]Nothing readable on this machine.[/]");
        }
        else
        {
            foreach (var r in report.Readings)
            {
                string colour = r.TemperatureC switch { >= 75 => "red", >= 55 => "yellow", _ => "teal" };
                string detail = string.IsNullOrEmpty(r.Detail) ? "" : $" [grey]({Markup.Escape(r.Detail)})[/]";
                AnsiConsole.MarkupLine($"[{colour}]{r.TemperatureC,5:0.0} C[/]  {Markup.Escape(r.Name)}{detail}");
            }
        }
        if (!report.AcpiThermalZoneSupported)
            AnsiConsole.MarkupLine("[grey]No CPU/system sensor on this machine. Windows exposes that only when the motherboard firmware reports it (mainly laptops).[/]");
        return 0;
    }
}

internal static class StorageCommand
{
    public static int Run(string[] args)
    {
        string path = args.FirstOrDefault(a => !a.StartsWith('-'))
            ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!Directory.Exists(path))
        {
            AnsiConsole.MarkupLineInterpolated($"[red]Not a folder:[/] {path}");
            return 1;
        }

        AnsiConsole.MarkupLineInterpolated($"[grey]Sizing[/] {path} [grey]...[/]");
        var entries = FolderSizer.ScanAsync(path).GetAwaiter().GetResult();
        long total = entries.Sum(e => e.SizeBytes);

        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("Size");
        table.AddColumn("Name");
        foreach (var e in entries.Take(30))
            table.AddRow(Markup.Escape(Bytes(e.SizeBytes)), (e.IsDirectory ? "[teal]" : "[grey]") + Markup.Escape(e.Name) + (e.IsDirectory ? "[/]" : "[/]"));
        AnsiConsole.Write(table);
        AnsiConsole.MarkupLineInterpolated($"[grey]Total:[/] {Bytes(total)} across {entries.Count} item(s)");
        return 0;
    }

    private static string Bytes(long b)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double v = b; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return $"{v:0.#} {u[i]}";
    }
}

internal static class ChangesCommand
{
    public static int Run(string[] args)
    {
        if (args.Contains("--snapshot"))
        {
            SystemSnapshot.Save(SystemSnapshot.Capture());
            AnsiConsole.MarkupLine("[green]Snapshot saved.[/]");
            return 0;
        }

        var list = SystemSnapshot.List();
        if (list.Count == 0)
        {
            SystemSnapshot.Save(SystemSnapshot.Capture());
            AnsiConsole.MarkupLine("[grey]Took a first snapshot. Run this again later to see what changed.[/]");
            return 0;
        }
        if (list.Count == 1)
        {
            AnsiConsole.MarkupLine("[grey]Only one snapshot so far. Run 'powerx changes --snapshot' after a change, or wait for the daily one.[/]");
            return 0;
        }

        var newer = SystemSnapshot.Load(list[0].Path);
        var older = SystemSnapshot.Load(list[^1].Path);
        for (int i = 1; i < list.Count; i++)
            if (list[0].When - list[i].When >= TimeSpan.FromHours(20)) { older = SystemSnapshot.Load(list[i].Path); break; }

        if (newer is null || older is null) { AnsiConsole.MarkupLine("[red]Could not read the snapshots.[/]"); return 1; }

        var diff = SystemSnapshot.Diff(older, newer);
        AnsiConsole.MarkupLineInterpolated(
            $"[grey]Comparing[/] {older.TakenAt.LocalDateTime:yyyy-MM-dd HH:mm} [grey]->[/] {newer.TakenAt.LocalDateTime:yyyy-MM-dd HH:mm}");
        if (!diff.Any)
        {
            AnsiConsole.MarkupLine("[green]No configuration changes.[/]");
            return 0;
        }
        foreach (var g in diff.Changes.GroupBy(c => c.Category))
        {
            AnsiConsole.MarkupLine($"\n[teal]{g.Key}[/]");
            foreach (var c in g)
            {
                string mark = c.Kind switch
                {
                    ChangeKind.Added => "[green]+[/]",
                    ChangeKind.Removed => "[red]-[/]",
                    _ => "[yellow]~[/]",
                };
                string detail = c.Kind switch
                {
                    ChangeKind.Added => Markup.Escape(c.After ?? ""),
                    ChangeKind.Removed => "gone (was " + Markup.Escape(c.Before ?? "") + ")",
                    _ => Markup.Escape(c.Before ?? "") + " [grey]->[/] " + Markup.Escape(c.After ?? ""),
                };
                AnsiConsole.MarkupLine($"  {mark} {Markup.Escape(c.Label)}  [grey]{detail}[/]");
            }
        }
        return 0;
    }
}
