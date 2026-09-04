using FluentAssertions;
using PowerX.Core.Diagnostics;
using PowerX.Core.Processes;
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
