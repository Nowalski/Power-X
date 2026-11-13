using FluentAssertions;
using PowerX.Core.Processes;
using Xunit;

namespace PowerX.Core.Tests;

/// <summary>
/// End process tree used to collect the tree inline and end every PID in it, PowerX included.
/// Run on the Explorer that started PowerX, it ended PowerX halfway through the list. These pin
/// the collection on its own, using a hand-built snapshot so nothing real is touched.
/// </summary>
public class ProcessTreeTests
{
    // Offset from our own PID so no made-up PID can ever be PowerX's (or the test host's) own.
    private static readonly int Base = Environment.ProcessId + 100_000;

    private static ProcessInfo P(int pid, int parent) => new()
    {
        Pid = pid, ParentPid = parent, Name = $"p{pid}",
        SessionId = 1, ThreadCount = 1, HandleCount = 1, BasePriority = 8,
        CpuPercent = 0, WorkingSetBytes = 0, PrivateBytes = 0, IoBytesPerSec = 0,
        HardFaultDelta = 0, StartTime = null,
    };

    private static ProcessSnapshot Snap(params ProcessInfo[] processes) =>
        new(processes, DateTimeOffset.UtcNow, processes.Length, processes.Length);

    [Fact]
    public void The_tree_comes_back_leaves_first()
    {
        int root = Base, a = Base + 4, a1 = Base + 8, b = Base + 12, other = Base + 16;
        var snap = Snap(P(root, 1), P(a, root), P(a1, a), P(b, root), P(other, 1));

        var pids = ProcessActions.TreePids(root, snap);

        pids.Should().Equal(b, a1, a, root);
        pids.Should().NotContain(other, "it is not in this tree");
    }

    [Fact]
    public void Every_child_comes_before_its_parent()
    {
        int root = Base, c1 = Base + 4, c2 = Base + 8, g1 = Base + 12, g2 = Base + 16, gg = Base + 20;
        var snap = Snap(P(gg, g2), P(g2, c2), P(root, 1), P(c1, root), P(c2, root), P(g1, c1));

        var pids = ProcessActions.TreePids(root, snap).ToList();

        pids.Should().HaveCount(6);
        pids[^1].Should().Be(root);
        foreach (var p in snap.Processes.Where(x => pids.Contains(x.Pid) && x.Pid != root))
            pids.IndexOf(p.Pid).Should().BeLessThan(pids.IndexOf(p.ParentPid),
                "PID {0} must be ended before its parent {1}", p.Pid, p.ParentPid);
    }

    [Fact]
    public void A_parent_pid_loop_from_pid_reuse_does_not_recurse_forever()
    {
        // x's parent PID was reused by y, which x started: each names the other as its parent.
        int x = Base, y = Base + 4;
        var snap = Snap(P(x, y), P(y, x));

        var pids = ProcessActions.TreePids(x, snap);

        pids.Should().Equal(y, x);
    }

    [Fact]
    public void A_process_that_names_itself_as_parent_is_listed_once()
    {
        int x = Base;
        var snap = Snap(P(x, x));

        ProcessActions.TreePids(x, snap).Should().Equal(x);
    }

    [Fact]
    public void PowerX_itself_and_what_it_started_are_never_in_the_tree()
    {
        int self = Environment.ProcessId;
        int explorer = Base, mine = Base + 4, sibling = Base + 8;
        var snap = Snap(P(explorer, 1), P(self, explorer), P(mine, self), P(sibling, explorer));

        var pids = ProcessActions.TreePids(explorer, snap);

        pids.Should().NotContain(self, "ending PowerX would stop it halfway through the tree");
        pids.Should().NotContain(mine, "PowerX may still be waiting on a process it started");
        pids.Should().Equal(sibling, explorer);
    }

    [Fact]
    public void Asking_for_PowerX_own_tree_gives_nothing_and_ending_it_is_refused()
    {
        int self = Environment.ProcessId;
        var snap = Snap(P(self, Base), P(Base + 4, self));

        ProcessActions.TreePids(self, snap).Should().BeEmpty();

        var result = ProcessActions.EndTaskTree(self, snap);
        result.Success.Should().BeFalse();
        result.Message.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void A_pid_missing_from_the_snapshot_is_still_its_own_tree()
    {
        // The row the user clicked can have exited between snapshots; the list is then just that PID.
        ProcessActions.TreePids(Base, Snap()).Should().Equal(Base);
    }
}

public class CriticalProcessTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void Idle_and_System_count_as_critical(int pid)
        => ProcessActions.IsCritical(pid).Should().BeTrue();

    [Fact]
    public void An_ordinary_process_is_not_critical()
        => ProcessActions.IsCritical(Environment.ProcessId).Should().BeFalse();

    [Fact]
    public void A_process_that_cannot_be_opened_reports_false_instead_of_throwing()
    {
        // Far beyond any PID Windows hands out, so OpenProcess fails.
        var act = () => ProcessActions.IsCritical(int.MaxValue);
        act.Should().NotThrow();
        act().Should().BeFalse();
    }

    [Fact]
    public void A_tree_whose_processes_cannot_be_opened_has_nothing_critical()
    {
        // A PID that cannot exist: opening it fails, which reports false.
        int bogus = int.MaxValue - 3;
        var snap = new ProcessSnapshot([], DateTimeOffset.UtcNow, 0, 0);
        ProcessActions.TreeHasCritical(bogus, snap).Should().BeFalse();
    }

    [Fact]
    public void A_tree_rooted_at_System_has_something_critical()
    {
        var snap = new ProcessSnapshot([], DateTimeOffset.UtcNow, 0, 0);
        ProcessActions.TreeHasCritical(4, snap).Should().BeTrue();
    }
}
