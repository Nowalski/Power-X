using FluentAssertions;
using PowerX.Core.Processes;
using PowerX.Core.Telemetry;
using PowerX.Core.Tweaks;
using Xunit;

namespace PowerX.Core.Tests;

public class TelemetryTests
{
    [Fact]
    public void MemoryProvider_returns_plausible_values()
    {
        var result = new MemoryMetricsProvider().Sample();
        result.HasValue.Should().BeTrue();
        var m = result.Value!;
        m.TotalPhysical.Should().BeGreaterThan(0);
        m.AvailablePhysical.Should().BeLessThanOrEqualTo(m.TotalPhysical);
        m.UsedPercent.Should().BeInRange(0, 100);
    }

    [Fact]
    public void CpuProvider_second_sample_is_in_range()
    {
        var p = new CpuMetricsProvider();
        p.Sample();
        Thread.Sleep(200);
        var r = p.Sample();
        r.Value!.TotalUsagePercent.Should().BeInRange(0, 100);
        r.Value.PerLogicalProcessor.Should().HaveCount(Environment.ProcessorCount);
        r.Value.PerLogicalProcessor.Should().OnlyContain(v => v >= 0 && v <= 100);
    }

    [Fact]
    public void ProcessProvider_finds_the_current_process()
    {
        var p = new ProcessProvider();
        var snap = p.Enumerate();
        snap.Processes.Should().Contain(x => x.Pid == Environment.ProcessId);
        snap.TotalProcesses.Should().BeGreaterThan(10);
        snap.Processes.Should().OnlyContain(x => x.CpuPercent >= 0 && x.CpuPercent <= 100);
    }

    [Fact]
    public void ProcessTree_has_no_orphan_cycles()
    {
        var snap = new ProcessProvider().Enumerate();
        var tree = ProcessProvider.BuildTree(snap);
        tree.Values.Sum(v => v.Count).Should().Be(snap.Processes.Count);
    }
}

public class CatalogTests
{
    [Fact]
    public void Every_tweak_has_unique_id_and_evidence()
    {
        var catalog = TweakCatalog.Default;
        catalog.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        catalog.Should().OnlyContain(t => t.Sources.Count > 0);
        catalog.Should().OnlyContain(t =>
            !string.IsNullOrWhiteSpace(t.WhatItDoes) &&
            !string.IsNullOrWhiteSpace(t.WhyYouMightWant) &&
            !string.IsNullOrWhiteSpace(t.Downside));
    }

    [Fact]
    public void Security_tradeoffs_are_never_recommended()
    {
        TweakCatalog.Default.Should().OnlyContain(t => t.Risk != TweakRisk.SecurityTradeoff || !t.Recommended);
    }

    [Fact]
    public void All_current_tweaks_detect_without_throwing()
    {
        var engine = new TweakEngine(TweakCatalog.Default);
        var act = () => engine.GetAllStatus();
        act.Should().NotThrow();
    }
}
