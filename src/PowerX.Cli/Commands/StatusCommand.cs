using PowerX.Core.Diagnostics;
using PowerX.Core.Telemetry;
using Spectre.Console;

namespace PowerX.Cli.Commands;

internal static class StatusCommand
{
    public static int Run(string[] args)
    {
        var info = SystemInfoProvider.Collect();

        var t = new Table().Border(TableBorder.Rounded).HideHeaders();
        t.AddColumn("k");
        t.AddColumn("v");
        t.AddRow("Windows", $"{info.WindowsEdition}  ({info.DisplayVersion}, build {info.BuildString})");
        t.AddRow("Architecture", info.Architecture);
        t.AddRow("Machine", info.MachineName + (info.IsElevated ? "  [yellow](elevated)[/]" : ""));
        t.AddRow("CPU", $"{info.CpuName}  · {info.LogicalProcessors} logical");
        t.AddRow("Memory", Format.Bytes(info.TotalPhysicalMemory));
        if (info.InstallDate is { } d) t.AddRow("Installed", d.LocalDateTime.ToString("yyyy-MM-dd"));
        AnsiConsole.Write(new Panel(t).Header("[teal]System[/]").Border(BoxBorder.None));

        var ci = CpuInfo.Query();
        var cput = new Table().Border(TableBorder.Rounded).HideHeaders();
        cput.AddColumn("k"); cput.AddColumn("v");
        cput.AddRow("Processor", $"{ci.Name}");
        cput.AddRow("Topology", $"{ci.Packages} socket · {ci.PhysicalCores} cores · {ci.LogicalProcessors} threads" +
            (ci.IsHybrid ? $" ({ci.PerformanceCores}P + {ci.EfficiencyCores}E)" : "") +
            (ci.HyperThreading ? " · SMT on" : ""));
        cput.AddRow("Clock", $"base {ci.BaseClockMhz / 1000.0:0.00} GHz · max {ci.MaxClockMhz / 1000.0:0.00} GHz");
        if (ci.L3 is { } l3) cput.AddRow("Cache", $"L1 {Format.Bytes(ci.L1?.TotalBytes ?? 0)} · L2 {Format.Bytes(ci.L2?.TotalBytes ?? 0)} · L3 {Format.Bytes(l3.TotalBytes)}");
        cput.AddRow("Virtualization", ci.VirtualizationFirmwareEnabled ? "[green]enabled in firmware[/]" : "[yellow]not enabled[/]");
        AnsiConsole.Write(new Panel(cput).Header("[teal]CPU[/]").Border(BoxBorder.None));

        var mh = MemoryHardware.Query();
        if (mh.Modules.Count > 0)
        {
            var mt = new Table().Border(TableBorder.Rounded);
            mt.AddColumn("Slot"); mt.AddColumn("Size"); mt.AddColumn("Type"); mt.AddColumn("Speed"); mt.AddColumn("Part");
            foreach (var dimm in mh.Modules)
                mt.AddRow(dimm.Slot, Format.Bytes(dimm.CapacityBytes), dimm.Type,
                    $"{(dimm.ConfiguredSpeedMtps > 0 ? dimm.ConfiguredSpeedMtps : dimm.SpeedMtps)} MT/s",
                    Markup.Escape($"{dimm.Manufacturer} {dimm.PartNumber}".Trim()));
            AnsiConsole.Write(new Panel(mt).Header(
                $"[teal]Memory[/]  {mh.DominantType} · {mh.EffectiveSpeedMtps} MT/s · {mh.SlotsUsed}/{mh.SlotsTotal} slots").Border(BoxBorder.None));
        }

        var cpu = new CpuMetricsProvider();
        var mem = new MemoryMetricsProvider();
        cpu.Sample(); // prime
        AnsiConsole.Status().Start("Sampling (1s)...", _ => Thread.Sleep(1000));

        var c = cpu.Sample();
        var m = mem.Sample();

        var live = new Table().Border(TableBorder.Rounded);
        live.AddColumn("Metric");
        live.AddColumn(new TableColumn("Value").RightAligned());
        live.AddColumn("Detail");

        if (c.HasValue)
        {
            var cm = c.Value!;
            live.AddRow("CPU", Format.Heat(cm.TotalUsagePercent),
                $"kernel {cm.KernelUsagePercent:0.0}% · {cm.ProcessCount} procs · {cm.ThreadCount} threads");
            live.AddRow("Uptime", Format.Duration(cm.Uptime), "");
        }
        else
        {
            live.AddRow("CPU", "[grey]unavailable[/]", c.Detail ?? "");
        }

        if (m.HasValue)
        {
            var mm = m.Value!;
            live.AddRow("Memory", Format.Heat(mm.UsedPercent),
                $"{Format.Bytes(mm.InUsePhysical)} / {Format.Bytes(mm.TotalPhysical)} · cached {Format.Bytes(mm.CachedApprox)}");
            live.AddRow("Commit", Format.Percent(mm.CommitPercent),
                $"{Format.Bytes(mm.CommitTotal)} / {Format.Bytes(mm.CommitLimit)}");
            if (mm.PagedPool > 0)
                live.AddRow("Kernel pool", "", $"paged {Format.Bytes(mm.PagedPool)} · non-paged {Format.Bytes(mm.NonPagedPool)}");
        }
        else
        {
            live.AddRow("Memory", "[grey]unavailable[/]", m.Detail ?? "");
        }

        using var gpu = new GpuMetricsProvider();
        gpu.Sample();
        Thread.Sleep(500);
        var g = gpu.Sample();
        if (g.HasValue)
        {
            var gm = g.Value!;
            live.AddRow("GPU", Format.Heat(gm.UtilizationPercent),
                $"{string.Join(" · ", gm.Engines.Take(3).Select(e => $"{e.Engine} {e.Percent:0}%"))} · VRAM {Format.Bytes(gm.DedicatedMemoryUsed)}");
        }
        else
        {
            live.AddRow("GPU", "[grey]unavailable[/]", g.Detail ?? "");
        }

        var net = new NetworkMetricsProvider();
        net.Sample();
        Thread.Sleep(500);
        var n = net.Sample();
        if (n.HasValue)
        {
            foreach (var iface in n.Value!.Interfaces.Where(i => i.IsUp).Take(3))
                live.AddRow($"Net: {iface.Name}", "",
                    $"↓ {Format.Rate(iface.ReceiveBytesPerSec)}  ↑ {Format.Rate(iface.SendBytesPerSec)}  · {iface.Type} {(iface.LinkSpeedBps > 0 ? $"{iface.LinkSpeedBps / 1_000_000} Mbps" : "")}");
        }

        AnsiConsole.Write(new Panel(live).Header("[teal]Live[/]").Border(BoxBorder.None));
        return 0;
    }
}
