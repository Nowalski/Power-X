using FluentAssertions;
using PowerX.Core.Startup;
using Xunit;

namespace PowerX.Core.Tests;

public class BootPerformanceTests
{
    [Fact]
    public void Read_never_throws_even_without_admin_rights()
    {
        var act = () => BootPerformance.Read();
        act.Should().NotThrow();
        // On a non-elevated CI runner the Diagnostics-Performance log is not readable, so a null
        // timeline and an empty list is the expected, correct result.
    }

    [Theory]
    [InlineData(0, StartupImpact.NotMeasured)]
    [InlineData(120, StartupImpact.Low)]
    [InlineData(500, StartupImpact.Medium)]
    [InlineData(2400, StartupImpact.High)]
    public void Impact_buckets_by_degradation_time(int degradationMs, StartupImpact expected)
    {
        var item = new BootItem
        {
            Name = "Test", Kind = BootItemKind.App,
            TotalMs = degradationMs + 100, DegradationMs = degradationMs,
            When = DateTimeOffset.Now,
        };
        item.Impact.Should().Be(expected);
    }

    private const string Ev100 = """
        <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>
          <System><EventID>100</EventID></System>
          <EventData>
            <Data Name='BootTime'>42100</Data>
            <Data Name='MainPathBootTime'>28900</Data>
            <Data Name='BootNumStartupApps'>9</Data>
            <Data Name='BootIsDegradation'>true</Data>
          </EventData>
        </Event>
        """;

    private const string Ev101 = """
        <Event xmlns='http://schemas.microsoft.com/win/2004/08/events/event'>
          <System><EventID>101</EventID></System>
          <EventData>
            <Data Name='Name'>Discord</Data>
            <Data Name='Path'>C:\Users\me\AppData\Local\Discord\Discord.exe</Data>
            <Data Name='TotalTime'>3100</Data>
            <Data Name='DegradationTime'>1600</Data>
          </EventData>
        </Event>
        """;

    [Fact]
    public void Parse_reads_boot_time_and_slow_apps()
    {
        var when = DateTimeOffset.Now;
        var (timeline, items) = BootPerformance.Parse(
            [(100, Ev100, when), (101, Ev101, when)], recentBoots: 12);

        timeline.Should().NotBeNull();
        timeline!.LastBootMs.Should().Be(42100);
        timeline.MainPathMs.Should().Be(28900);
        timeline.StartupAppCount.Should().Be(9);
        timeline.Degraded.Should().BeTrue();

        items.Should().ContainSingle();
        items[0].Name.Should().Be("Discord");
        items[0].Path.Should().EndWith("Discord.exe");
        items[0].DegradationMs.Should().Be(1600);
        items[0].Impact.Should().Be(StartupImpact.High);

        timeline.Recent.Should().ContainSingle();
        timeline.FastestBootMs.Should().Be(42100);
        timeline.SlowestBootMs.Should().Be(42100);
    }

    [Fact]
    public void Parse_builds_a_recent_boot_trend_newest_first()
    {
        var now = DateTimeOffset.Now;
        string Boot(int ms) => Ev100.Replace("42100", ms.ToString());
        var events = new List<(int, string, DateTimeOffset)>
        {
            (100, Boot(30000), now),
            (100, Boot(45000), now.AddDays(-1)),
            (100, Boot(38000), now.AddDays(-2)),
        };
        var (timeline, _) = BootPerformance.Parse(events, recentBoots: 12);
        timeline!.Recent.Select(r => r.TotalMs).Should().Equal(30000, 45000, 38000);
        timeline.FastestBootMs.Should().Be(30000);
        timeline.SlowestBootMs.Should().Be(45000);
        timeline.LastBootMs.Should().Be(30000);
    }

    [Fact]
    public void Parse_still_lists_slow_apps_when_the_boot_marker_is_missing()
    {
        var (timeline, items) = BootPerformance.Parse([(101, Ev101, DateTimeOffset.Now)], recentBoots: 12);
        timeline.Should().BeNull("there was no event 100 to build an overall boot time from");
        items.Should().ContainSingle().Which.Name.Should().Be("Discord");
    }

    [Fact]
    public void Parse_stops_after_the_requested_number_of_boots()
    {
        var when = DateTimeOffset.Now;
        // three boot markers, one slow app each; ask for only the newest two
        var events = new List<(int, string, DateTimeOffset)>
        {
            (101, Ev101, when), (100, Ev100, when),
            (101, Ev101, when.AddDays(-1)), (100, Ev100, when.AddDays(-1)),
            (101, Ev101, when.AddDays(-2)), (100, Ev100, when.AddDays(-2)),
        };
        var (_, items) = BootPerformance.Parse(events, recentBoots: 2);
        // only the recent window is walked, and the repeated "Discord" collapses to one row
        items.Should().ContainSingle();
    }
}
