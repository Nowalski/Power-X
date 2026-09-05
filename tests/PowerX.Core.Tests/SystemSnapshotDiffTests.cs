using FluentAssertions;
using PowerX.Core.Diagnostics;
using Xunit;

namespace PowerX.Core.Tests;

public class SystemSnapshotDiffTests
{
    private static ConfigSnapshot Snap(DateTimeOffset when, params (SnapshotCategory Cat, string Key, string Label, string State)[] items)
        => new()
        {
            TakenAt = when,
            Items = items
                .GroupBy(i => i.Cat)
                .ToDictionary(g => g.Key, g => g.Select(i => new SnapshotItem(i.Key, i.Label, i.State)).ToList()),
        };

    private static readonly DateTimeOffset T1 = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void An_entry_only_in_the_newer_snapshot_is_Added()
    {
        var diff = SystemSnapshot.Diff(
            Snap(T1),
            Snap(T2, (SnapshotCategory.Startup, "run:foo", "Foo", "enabled")));

        diff.Any.Should().BeTrue();
        diff.Changes.Should().ContainSingle();
        diff.Changes[0].Kind.Should().Be(ChangeKind.Added);
        diff.Changes[0].Label.Should().Be("Foo");
        diff.Changes[0].Before.Should().BeNull();
        diff.Changes[0].After.Should().Be("enabled");
    }

    [Fact]
    public void An_entry_only_in_the_older_snapshot_is_Removed()
    {
        var diff = SystemSnapshot.Diff(
            Snap(T1, (SnapshotCategory.Program, "app|user", "App", "1.0")),
            Snap(T2));

        diff.Changes.Should().ContainSingle();
        diff.Changes[0].Kind.Should().Be(ChangeKind.Removed);
        diff.Changes[0].Before.Should().Be("1.0");
        diff.Changes[0].After.Should().BeNull();
    }

    [Fact]
    public void A_changed_state_reports_both_sides()
    {
        var diff = SystemSnapshot.Diff(
            Snap(T1, (SnapshotCategory.Driver, "gpu", "GPU", "1.0.0")),
            Snap(T2, (SnapshotCategory.Driver, "gpu", "GPU", "2.0.0")));

        diff.Changes.Should().ContainSingle();
        diff.Changes[0].Kind.Should().Be(ChangeKind.Changed);
        diff.Changes[0].Before.Should().Be("1.0.0");
        diff.Changes[0].After.Should().Be("2.0.0");
    }

    [Fact]
    public void An_unchanged_entry_produces_nothing()
    {
        var diff = SystemSnapshot.Diff(
            Snap(T1, (SnapshotCategory.Service, "svc", "Service", "Automatic")),
            Snap(T2, (SnapshotCategory.Service, "svc", "Service", "Automatic")));

        diff.Any.Should().BeFalse();
        diff.Changes.Should().BeEmpty();
    }

    [Fact]
    public void State_comparison_ignores_case_so_a_reformatted_value_is_not_a_false_change()
    {
        var diff = SystemSnapshot.Diff(
            Snap(T1, (SnapshotCategory.Startup, "run:foo", "Foo", "Enabled")),
            Snap(T2, (SnapshotCategory.Startup, "run:foo", "Foo", "enabled")));

        diff.Changes.Should().BeEmpty();
    }

    [Fact]
    public void The_same_key_in_two_categories_is_tracked_separately()
    {
        // A service and a scheduled task can share a name; they must not cancel each other out.
        var diff = SystemSnapshot.Diff(
            Snap(T1, (SnapshotCategory.Service, "same", "Same", "Automatic")),
            Snap(T2, (SnapshotCategory.ScheduledTask, "same", "Same", "enabled")));

        diff.Changes.Should().HaveCount(2);
        diff.Changes.Should().Contain(c => c.Category == SnapshotCategory.ScheduledTask && c.Kind == ChangeKind.Added);
        diff.Changes.Should().Contain(c => c.Category == SnapshotCategory.Service && c.Kind == ChangeKind.Removed);
    }

    [Fact]
    public void A_duplicate_key_collapses_instead_of_throwing()
    {
        // Real installers do register the same display name twice; ToDictionary would throw here.
        var from = Snap(T1,
            (SnapshotCategory.Program, "dup", "Dup", "1.0"),
            (SnapshotCategory.Program, "dup", "Dup", "1.0"));
        var to = Snap(T2,
            (SnapshotCategory.Program, "dup", "Dup", "2.0"),
            (SnapshotCategory.Program, "dup", "Dup", "2.0"));

        var act = () => SystemSnapshot.Diff(from, to);

        act.Should().NotThrow();
        act().Changes.Should().ContainSingle().Which.Kind.Should().Be(ChangeKind.Changed);
    }

    [Fact]
    public void A_category_missing_entirely_from_one_side_is_handled()
    {
        // An older snapshot taken before a category existed, or one whose source failed.
        var diff = SystemSnapshot.Diff(
            Snap(T1, (SnapshotCategory.Startup, "run:foo", "Foo", "enabled")),
            Snap(T2, (SnapshotCategory.Startup, "run:foo", "Foo", "enabled"),
                     (SnapshotCategory.Tweak, "tw", "Tweak", "applied")));

        diff.Changes.Should().ContainSingle();
        diff.Changes[0].Category.Should().Be(SnapshotCategory.Tweak);
        diff.Changes[0].Kind.Should().Be(ChangeKind.Added);
    }

    [Fact]
    public void Changes_come_back_in_a_stable_order_regardless_of_input_order()
    {
        var from = Snap(T1,
            (SnapshotCategory.Program, "b", "Beta", "1.0"),
            (SnapshotCategory.Program, "a", "Alpha", "1.0"));
        var to = Snap(T2,
            (SnapshotCategory.Program, "a", "Alpha", "2.0"),
            (SnapshotCategory.Program, "b", "Beta", "2.0"));

        var labels = SystemSnapshot.Diff(from, to).Changes.Select(c => c.Label).ToList();

        labels.Should().Equal("Alpha", "Beta");
    }

    [Fact]
    public void The_diff_carries_both_snapshot_timestamps()
    {
        var diff = SystemSnapshot.Diff(Snap(T1), Snap(T2));

        diff.FromWhen.Should().Be(T1);
        diff.ToWhen.Should().Be(T2);
    }
}
