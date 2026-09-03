using FluentAssertions;
using PowerX.Core.Telemetry;
using Xunit;

namespace PowerX.Core.Tests;

public class MetricRingTests
{
    [Fact]
    public void Overwrites_oldest_when_full_and_stays_bounded()
    {
        var ring = new MetricRing(3);
        for (int i = 1; i <= 5; i++) ring.Add(i);
        ring.Count.Should().Be(3);
        ring.ToArray().Should().Equal(3, 4, 5);
        ring.Latest.Should().Be(5);
        ring.Max().Should().Be(5);
    }

    [Fact]
    public void Partial_fill_reports_only_real_samples()
    {
        var ring = new MetricRing(10);
        ring.Add(20);
        ring.Add(40);
        ring.Count.Should().Be(2);
        ring.Average().Should().Be(30);
        ring.ToArray().Should().Equal(20, 40);
    }

    [Fact]
    public void Zero_capacity_is_rejected()
    {
        var act = () => new MetricRing(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Seed_fills_an_empty_ring_and_is_a_noop_once_samples_exist()
    {
        var ring = new MetricRing(4);
        ring.Seed(7);
        ring.Count.Should().Be(4);
        ring.ToArray().Should().Equal(7, 7, 7, 7);

        ring.Add(9);
        ring.ToArray().Should().Equal(7, 7, 7, 9);

        ring.Seed(1); // ignored — already has samples
        ring.ToArray().Should().Equal(7, 7, 7, 9);
    }
}
