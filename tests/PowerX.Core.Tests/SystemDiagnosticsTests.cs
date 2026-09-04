using FluentAssertions;
using PowerX.Core.Diagnostics;
using Xunit;

namespace PowerX.Core.Tests;

public class SystemDiagnosticsTests
{
    [Fact]
    public void PendingReboot_never_throws()
    {
        var act = () => PendingReboot.Check();
        act.Should().NotThrow();
    }

    [Fact]
    public void ComponentStore_parses_a_dism_report()
    {
        const string dism = """
            Deployment Image Servicing and Management tool

            Component Store (WinSxS) information:

            Windows Explorer Reported Size of Component Store : 8.42 GB
            Actual Size of Component Store : 8.15 GB
                Shared with Windows : 5.90 GB
                Backups and Disabled Features : 1.80 GB
                Cache and Temporary Data : 0.45 GB
            Date of Last Cleanup : 2026-08-01 03:14:22
            Number of Reclaimable Packages : 12
            Component Store Cleanup Recommended : Yes

            The operation completed successfully.
            """;
        var info = ComponentStore.Parse(dism);
        info.ActualSizeBytes.Should().BeInRange((long)(8.15 * 1024 * 1024 * 1024) - 5, (long)(8.15 * 1024 * 1024 * 1024) + 5);
        info.SharedWithWindowsBytes.Should().BeGreaterThan(info.BackupsAndDisabledBytes);
        info.BackupsAndDisabledBytes.Should().Be((long)(1.80 * 1024 * 1024 * 1024));
        info.ReclaimablePackages.Should().Be(12);
        info.CleanupRecommended.Should().BeTrue();
        info.LastCleanup.Should().NotBeNull();
        info.PotentialSavingsBytes.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ComponentStore_handles_a_report_it_could_not_read()
    {
        ComponentStore.Parse("Error: 1392").ReclaimablePackages.Should().Be(0);
    }

    [Fact]
    public void BatteryReport_parses_capacity_and_cycles()
    {
        const string xml = """
            <?xml version="1.0"?>
            <BatteryReport xmlns="http://schemas.microsoft.com/battery/2012">
              <Batteries>
                <Battery>
                  <Id>ABC123</Id>
                  <Manufacturer>LGC</Manufacturer>
                  <Chemistry>LiP</Chemistry>
                  <DesignCapacity>60000</DesignCapacity>
                  <FullChargeCapacity>51000</FullChargeCapacity>
                  <CycleCount>214</CycleCount>
                </Battery>
              </Batteries>
            </BatteryReport>
            """;
        var info = BatteryHealth.ParseReport(xml);
        info.DesignCapacityMwh.Should().Be(60000);
        info.FullChargeCapacityMwh.Should().Be(51000);
        info.CycleCount.Should().Be(214);
        info.WearPercent.Should().Be(15);
        info.Health.Should().Be("good");
    }

    [Fact]
    public void BatteryHealth_read_never_throws()
    {
        var act = () => BatteryHealth.ReadAsync().GetAwaiter().GetResult();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task FolderSizer_sizes_the_immediate_children()
    {
        string root = Path.Combine(Path.GetTempPath(), "powerx-sizer-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "big", "nested"));
        Directory.CreateDirectory(Path.Combine(root, "small"));
        await File.WriteAllBytesAsync(Path.Combine(root, "big", "nested", "a.bin"), new byte[50_000]);
        await File.WriteAllBytesAsync(Path.Combine(root, "small", "b.bin"), new byte[1_000]);
        await File.WriteAllBytesAsync(Path.Combine(root, "loose.bin"), new byte[10_000]);

        try
        {
            var entries = await FolderSizer.ScanAsync(root);
            entries.Should().HaveCount(3);
            entries[0].Name.Should().Be("big");
            entries[0].SizeBytes.Should().BeGreaterThanOrEqualTo(50_000);
            entries[0].IsDirectory.Should().BeTrue();
            entries.Single(e => e.Name == "loose.bin").IsDirectory.Should().BeFalse();
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }

    [Fact]
    public void SnapshotDiff_reports_added_removed_and_changed()
    {
        ConfigSnapshot Snap(params (SnapshotCategory cat, string key, string label, string state)[] items) => new()
        {
            TakenAt = DateTimeOffset.Now,
            Items = items.GroupBy(i => i.cat).ToDictionary(
                g => g.Key,
                g => g.Select(i => new SnapshotItem(i.key, i.label, i.state)).ToList()),
        };

        var before = Snap(
            (SnapshotCategory.Startup, "a", "Alpha", "enabled"),
            (SnapshotCategory.Startup, "b", "Beta", "enabled"),
            (SnapshotCategory.Program, "p", "Prog", "1.0"));

        var after = Snap(
            (SnapshotCategory.Startup, "a", "Alpha", "disabled"),   // changed
            (SnapshotCategory.Program, "p", "Prog", "1.0"),          // same
            (SnapshotCategory.Program, "q", "New", "2.0"));          // added; b removed

        var diff = SystemSnapshot.Diff(before, after);
        diff.Changes.Should().Contain(c => c.Label == "Alpha" && c.Kind == ChangeKind.Changed && c.Before == "enabled" && c.After == "disabled");
        diff.Changes.Should().Contain(c => c.Label == "Beta" && c.Kind == ChangeKind.Removed);
        diff.Changes.Should().Contain(c => c.Label == "New" && c.Kind == ChangeKind.Added);
        diff.Changes.Should().NotContain(c => c.Label == "Prog");
    }
}
