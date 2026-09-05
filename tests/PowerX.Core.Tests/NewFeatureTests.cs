using FluentAssertions;
using PowerX.Core.Diagnostics;
using PowerX.Core.Processes;
using PowerX.Core.Telemetry;
using Xunit;

namespace PowerX.Core.Tests;

public class ProcessKnowledgeTests
{
    [Fact]
    public void Explains_a_well_known_process()
    {
        var e = ProcessKnowledge.Explain("svchost.exe", @"C:\Windows\System32\svchost.exe", "Microsoft Corporation");
        e.KnownGood.Should().BeTrue();
        e.Summary.Should().Contain("service");
    }

    [Fact]
    public void Treats_a_microsoft_signed_system_binary_as_normal_even_if_unlisted()
    {
        var e = ProcessKnowledge.Explain("SomeObscureTool.exe", @"C:\Windows\System32\SomeObscureTool.exe", "Microsoft Corporation");
        e.KnownGood.Should().BeTrue();
    }

    [Fact]
    public void Gives_no_verdict_on_an_unknown_unsigned_process()
    {
        var e = ProcessKnowledge.Explain("mystery.exe", @"C:\Users\me\Downloads\mystery.exe", null);
        e.KnownGood.Should().BeFalse();
        e.Summary.Should().Contain("Security page");
    }
}

public class HealthCheckTests
{
    [Fact]
    public async Task ScanAsync_never_throws_and_always_returns_a_report()
    {
        var report = await HealthCheck.ScanAsync(deep: false);
        report.Should().NotBeNull();
        report.Items.Should().NotBeNull();
        report.Score.Should().BeInRange(0, 100);
    }

    [Fact]
    public void HealthReport_score_drops_with_more_and_higher_impact_items()
    {
        Recommendation R(RecommendationImpact i) => new() { Category = "x", Title = "x", Detail = "x", Impact = i };

        var clean = new HealthReport { When = DateTimeOffset.Now, Items = [], Deep = false };
        clean.Score.Should().Be(100);

        var bad = new HealthReport { When = DateTimeOffset.Now, Deep = false, Items = [R(RecommendationImpact.High), R(RecommendationImpact.High), R(RecommendationImpact.Medium)] };
        bad.Score.Should().BeLessThan(clean.Score);
        bad.High.Should().Be(2);
        bad.Medium.Should().Be(1);
    }
}

public class GpuAdapterLuidTests
{
    // Real instance names captured from a live multi-GPU machine (\GPU Engine and
    // \GPU Adapter Memory counters) — see D-031.
    [Theory]
    [InlineData("pid_11360_luid_0x00000000_0x0001E92C_phys_0_eng_0_engtype_3D", 0x0001E92CL)]
    [InlineData("luid_0x00000002_0xBEB2AD3D_phys_0", 0x2BEB2AD3DL)]
    [InlineData("pid_4_luid_0x00000000_0x0001E97A_phys_0_eng_5_engtype_Copy", 0x0001E97AL)]
    public void ParseLuid_extracts_the_luid_from_a_real_pdh_instance_name(string instance, long expected)
    {
        GpuMetricsProvider.ParseLuid(instance).Should().Be(expected);
    }

    [Theory]
    [InlineData("")]
    [InlineData("no luid here")]
    [InlineData("luid_0xZZZZZZZZ_0x00000001_phys_0")]
    public void ParseLuid_returns_zero_for_anything_unparseable(string instance)
    {
        GpuMetricsProvider.ParseLuid(instance).Should().Be(0);
    }
}
