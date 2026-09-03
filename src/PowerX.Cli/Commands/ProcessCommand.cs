using PowerX.Core.Processes;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class ProcessCommand
{
    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] != "list")
        {
            AnsiConsole.MarkupLine("usage: [teal]powerx process list[/] [[--sort cpu|mem|io|name]] [[--top N]] [[--tree]]");
            return 1;
        }

        string sort = Arg(args, "--sort") ?? "cpu";
        int top = int.TryParse(Arg(args, "--top"), out var n) ? n : 20;
        bool tree = args.Contains("--tree");

        var provider = new ProcessProvider();
        provider.Enumerate();                 // prime for CPU deltas
        Thread.Sleep(1000);
        var snap = provider.Enumerate();

        var ranked = snap.Processes.Where(p => p.Pid > 0);
        ranked = sort switch
        {
            "mem" => ranked.OrderByDescending(p => p.WorkingSetBytes),
            "io" => ranked.OrderByDescending(p => p.IoBytesPerSec),
            "name" => ranked.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase),
            _ => ranked.OrderByDescending(p => p.CpuPercent).ThenByDescending(p => p.WorkingSetBytes),
        };

        AnsiConsole.MarkupLineInterpolated(
            $"[grey]{snap.TotalProcesses} processes · {snap.TotalThreads} threads · sampled {snap.Timestamp.LocalDateTime:HH:mm:ss}[/]");

        if (tree)
        {
            RenderTree(snap);
            return 0;
        }

        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.AddColumn(new TableColumn("PID").RightAligned());
        table.AddColumn("Name");
        table.AddColumn(new TableColumn("CPU").RightAligned());
        table.AddColumn(new TableColumn("Working set").RightAligned());
        table.AddColumn(new TableColumn("Private").RightAligned());
        table.AddColumn(new TableColumn("I/O").RightAligned());
        table.AddColumn(new TableColumn("Thr").RightAligned());
        table.AddColumn(new TableColumn("Hnd").RightAligned());

        foreach (var p in ranked.Take(top))
        {
            table.AddRow(
                p.Pid.ToString(),
                Markup.Escape(p.Name),
                Format.Heat(p.CpuPercent),
                Format.Bytes(p.WorkingSetBytes),
                Format.Bytes(p.PrivateBytes),
                Format.Rate(p.IoBytesPerSec),
                p.ThreadCount.ToString(),
                p.HandleCount.ToString());
        }
        AnsiConsole.Write(table);
        return 0;
    }

    private static void RenderTree(ProcessSnapshot snap)
    {
        var byParent = ProcessProvider.BuildTree(snap);
        var root = new Tree("[teal]Processes[/]");
        foreach (var p in (byParent.GetValueOrDefault(-1) ?? []).OrderByDescending(p => p.CpuPercent).Take(15))
        {
            AddNode(root.AddNode(Label(p)), p.Pid, byParent, 0);
        }
        AnsiConsole.Write(root);

        static void AddNode(TreeNode node, int pid, IReadOnlyDictionary<int, List<ProcessInfo>> map, int depth)
        {
            if (depth > 6 || !map.TryGetValue(pid, out var kids)) return;
            foreach (var k in kids.OrderByDescending(k => k.CpuPercent))
            {
                AddNode(node.AddNode(Label(k)), k.Pid, map, depth + 1);
            }
        }

        static string Label(ProcessInfo p) =>
            $"{Markup.Escape(p.Name)} [grey]({p.Pid})[/]  {p.CpuPercent:0.0}%  {Format.Bytes(p.WorkingSetBytes)}";
    }

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
