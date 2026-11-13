using FluentAssertions;
using Microsoft.Win32;
using PowerX.Core.Diagnostics;
using PowerX.Core.Services;
using PowerX.Core.Startup;
using Xunit;

namespace PowerX.Core.Tests;

/// <summary>
/// PowerX is a 64-bit process, so it never saw HKLM\SOFTWARE\WOW6432Node\...\Run, where 32-bit
/// installers register all-users startup entries (Task Manager lists them). These pin how the
/// two new sources behave. Nothing here writes to HKLM: everything that would is checked through
/// the location helpers or refused before any registry access.
/// </summary>
public class StartupWow64Tests
{
    private static StartupEntry Entry(StartupSource source, bool broken = false, string? exe = null) => new()
    {
        Name = $"PowerXTest_{Guid.NewGuid():N}", Command = exe ?? "app.exe", Source = source,
        Enabled = true, Broken = broken, ExecutablePath = exe,
    };

    [Fact]
    public void The_32_bit_sources_have_their_own_labels()
    {
        Entry(StartupSource.RunMachine32).SourceLabel.Should().Be("Registry · all users (32-bit)");
        Entry(StartupSource.RunOnceMachine32).SourceLabel.Should().Be("Registry · run once (all users, 32-bit)");

        Entry(StartupSource.RunMachine32).SourceLabel.Should().NotBe(Entry(StartupSource.RunMachine).SourceLabel,
            "the Startup page groups by label, so the two must not merge into one group");
    }

    [Theory]
    [InlineData(StartupSource.RunMachine32)]
    [InlineData(StartupSource.RunOnceMachine32)]
    public void The_32_bit_sources_are_machine_wide_and_need_admin(StartupSource source)
        => Entry(source).RequiresAdmin.Should().BeTrue();

    [Fact]
    public void The_32_bit_Run_entry_toggles_like_its_64_bit_counterpart()
    {
        StartupProvider.CanToggle(Entry(StartupSource.RunMachine32)).Should().BeTrue();
        StartupProvider.CanToggle(Entry(StartupSource.RunMachine)).Should().BeTrue();
    }

    [Fact]
    public void The_32_bit_RunOnce_entry_cannot_be_toggled_and_the_attempt_is_refused()
    {
        var entry = Entry(StartupSource.RunOnceMachine32);

        StartupProvider.CanToggle(entry).Should().BeFalse();
        // Refused before any registry access, so this writes nothing.
        StartupProvider.SetEnabled(entry, false).Success.Should().BeFalse();
    }

    [Fact]
    public void The_32_bit_RunOnce_entry_can_always_be_removed()
        => StartupProvider.CanRemove(Entry(StartupSource.RunOnceMachine32)).Should().BeTrue();

    [Fact]
    public void A_32_bit_Run_entry_can_be_removed_only_when_broken()
    {
        StartupProvider.CanRemove(Entry(StartupSource.RunMachine32, broken: true)).Should().BeTrue();
        StartupProvider.CanRemove(Entry(StartupSource.RunMachine32, broken: false)).Should().BeFalse();
    }

    [Fact]
    public void Removing_a_working_32_bit_Run_entry_is_refused_without_touching_the_registry()
        => StartupProvider.Remove(Entry(StartupSource.RunMachine32)).Success.Should().BeFalse();

    [Fact]
    public void Remove_deletes_from_the_WOW6432Node_key_the_entry_came_from()
    {
        StartupProvider.RegistryLocation(StartupSource.RunMachine32)
            .Should().Be((true, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"));
        StartupProvider.RegistryLocation(StartupSource.RunOnceMachine32)
            .Should().Be((true, @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\RunOnce"));

        // and the 64-bit keys did not move
        StartupProvider.RegistryLocation(StartupSource.RunMachine)
            .Should().Be((true, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"));
        StartupProvider.RegistryLocation(StartupSource.RunUser)
            .Should().Be((false, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run"));
    }

    [Fact]
    public void On_off_state_for_32_bit_entries_lives_in_StartupApproved_Run32_under_HKLM()
    {
        StartupProvider.ApprovedLocation(StartupSource.RunMachine32)
            .Should().Be((true, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32"));
        StartupProvider.ApprovedLocation(StartupSource.RunMachine)
            .Should().Be((true, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"));

        StartupProvider.ApprovedLocation(StartupSource.RunOnceMachine32).Should().BeNull("RunOnce is not governed by StartupApproved");
        StartupProvider.ApprovedLocation(StartupSource.ScheduledTask).Should().BeNull();
    }

    [Fact]
    public void A_32_bit_Run_entry_with_a_real_program_can_be_delayed()
    {
        string cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        StartupDelay.CanDelay(Entry(StartupSource.RunMachine32, exe: cmd)).Should().BeTrue();
        StartupDelay.CanDelay(Entry(StartupSource.RunOnceMachine32, exe: cmd)).Should().BeFalse();
    }

    [Fact]
    public void Every_value_in_the_32_bit_Run_key_is_listed_as_a_32_bit_entry()
    {
        // Read-only. Compares Enumerate against the key itself, so it holds on any PC, including
        // one with no 32-bit entries at all.
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run");
        var expected = (key?.GetValueNames() ?? []).Where(n => n.Length > 0).ToList();

        var listed = StartupProvider.Enumerate()
            .Where(e => e.Source == StartupSource.RunMachine32)
            .Select(e => e.Name)
            .ToList();

        listed.Should().BeEquivalentTo(expected);
    }
}

/// <summary>
/// How snapshots treat the 32-bit Run key. Snapshots saved before PowerX read it have no record
/// of those entries, so a naive diff against the first new snapshot lists every one of them as
/// "added" although nothing on the PC changed.
/// </summary>
public class SnapshotStartupCoverageTests
{
    private static readonly DateTimeOffset T1 = new(2026, 9, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset T2 = new(2026, 9, 5, 10, 0, 0, TimeSpan.Zero);

    private static StartupEntry Entry(StartupSource source, string name, bool enabled = true) => new()
    {
        Name = name, Command = "app.exe", Source = source, Enabled = enabled,
    };

    /// <summary>A snapshot as written before StartupSources existed: the property is absent.</summary>
    private static ConfigSnapshot Legacy(DateTimeOffset when, params StartupEntry[] entries) => new()
    {
        TakenAt = when,
        Items = new() { [SnapshotCategory.Startup] = entries.Select(SystemSnapshot.StartupItem).ToList() },
    };

    private static ConfigSnapshot Current(DateTimeOffset when, params StartupEntry[] entries) => new()
    {
        TakenAt = when,
        Items = new() { [SnapshotCategory.Startup] = entries.Select(SystemSnapshot.StartupItem).ToList() },
        StartupSources = SystemSnapshot.SnapshotStartupSources.Select(s => s.ToString()).ToList(),
    };

    [Fact]
    public void A_32_bit_entry_does_not_collide_with_a_64_bit_entry_of_the_same_name()
    {
        var a = SystemSnapshot.StartupItem(Entry(StartupSource.RunMachine, "Discord"));
        var b = SystemSnapshot.StartupItem(Entry(StartupSource.RunMachine32, "Discord"));

        a.Key.Should().NotBe(b.Key);
    }

    [Fact]
    public void Keys_for_existing_sources_keep_their_old_spelling()
    {
        // Older snapshots on disk use this spelling; a change here would make every entry look
        // removed and re-added.
        SystemSnapshot.StartupItem(Entry(StartupSource.RunMachine, "Discord")).Key.Should().Be("runmachine:discord");
        SystemSnapshot.StartupItem(Entry(StartupSource.RunUser, "OneDrive")).Key.Should().Be("runuser:onedrive");
    }

    [Fact]
    public void Snapshots_cover_the_32_bit_Run_key_but_not_RunOnce_or_tasks()
    {
        SystemSnapshot.SnapshotStartupSources.Should().Contain(StartupSource.RunMachine32);
        SystemSnapshot.SnapshotStartupSources.Should().NotContain(
            [StartupSource.RunOnceUser, StartupSource.RunOnceMachine, StartupSource.RunOnceMachine32, StartupSource.ScheduledTask]);
        SystemSnapshot.LegacyStartupSources.Should().NotContain(StartupSource.RunMachine32);
    }

    [Fact]
    public void First_diff_after_the_update_does_not_report_existing_32_bit_entries_as_added()
    {
        var before = Legacy(T1, Entry(StartupSource.RunMachine, "Realtek"));
        var after = Current(T2,
            Entry(StartupSource.RunMachine, "Realtek"),
            Entry(StartupSource.RunMachine32, "Discord"),
            Entry(StartupSource.RunMachine32, "Adobe Updater"));

        SystemSnapshot.Diff(before, after).Changes.Should().BeEmpty();
    }

    [Fact]
    public void A_real_addition_in_a_source_the_old_snapshot_covered_is_still_reported()
    {
        var before = Legacy(T1, Entry(StartupSource.RunMachine, "Realtek"));
        var after = Current(T2,
            Entry(StartupSource.RunMachine, "Realtek"),
            Entry(StartupSource.RunMachine, "NewApp"),
            Entry(StartupSource.RunMachine32, "Discord"));

        var diff = SystemSnapshot.Diff(before, after);

        diff.Changes.Should().ContainSingle();
        diff.Changes[0].Kind.Should().Be(ChangeKind.Added);
        diff.Changes[0].Label.Should().Be("NewApp");
    }

    [Fact]
    public void Between_two_new_snapshots_32_bit_changes_are_reported()
    {
        var before = Current(T1, Entry(StartupSource.RunMachine32, "Discord"), Entry(StartupSource.RunMachine32, "Old"));
        var after = Current(T2, Entry(StartupSource.RunMachine32, "Discord", enabled: false), Entry(StartupSource.RunMachine32, "New"));

        var diff = SystemSnapshot.Diff(before, after);

        diff.Changes.Should().HaveCount(3);
        diff.Changes.Should().Contain(c => c.Label == "Discord" && c.Kind == ChangeKind.Changed && c.After == "disabled");
        diff.Changes.Should().Contain(c => c.Label == "New" && c.Kind == ChangeKind.Added);
        diff.Changes.Should().Contain(c => c.Label == "Old" && c.Kind == ChangeKind.Removed);
    }

    [Fact]
    public void Comparing_backwards_against_an_older_snapshot_does_not_report_32_bit_entries_as_removed()
    {
        var newer = Current(T1, Entry(StartupSource.RunMachine, "Realtek"), Entry(StartupSource.RunMachine32, "Discord"));
        var older = Legacy(T2, Entry(StartupSource.RunMachine, "Realtek"));

        SystemSnapshot.Diff(newer, older).Changes.Should().BeEmpty();
    }

    [Fact]
    public void A_snapshot_saved_before_the_update_loads_with_no_recorded_sources()
    {
        string path = Path.Combine(Path.GetTempPath(), $"powerx-snap-{Guid.NewGuid():N}.json");
        File.WriteAllText(path,
            """{"TakenAt":"2026-09-01T10:00:00+00:00","WindowsBuild":"26200","Automatic":true,"Items":{"Startup":[{"Key":"runmachine:realtek","Label":"Realtek","State":"enabled"}]}}""");
        try
        {
            var snap = SystemSnapshot.Load(path);
            snap.Should().NotBeNull();
            snap!.StartupSources.Should().BeNull();
            snap.Items[SnapshotCategory.Startup].Should().ContainSingle();
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_snapshot_from_a_newer_PowerX_naming_an_unknown_source_still_loads()
    {
        string path = Path.Combine(Path.GetTempPath(), $"powerx-snap-{Guid.NewGuid():N}.json");
        File.WriteAllText(path,
            """{"TakenAt":"2026-09-01T10:00:00+00:00","Items":{},"StartupSources":["RunUser","SomethingNew"]}""");
        try
        {
            SystemSnapshot.Load(path)!.StartupSources.Should().Equal("RunUser", "SomethingNew");
        }
        finally { File.Delete(path); }
    }
}

public class ServiceStartModeTests
{
    /// <summary>
    /// SetStartMode now goes through the Service Control Manager. A name that cannot exist must
    /// come back as a plain failure, not an exception, and nothing on the machine changes.
    /// </summary>
    [Fact]
    public void A_service_that_does_not_exist_fails_cleanly()
    {
        string name = $"PowerXNoSuchService{Guid.NewGuid():N}";

        var act = () => ServiceProvider.SetStartMode(name, ServiceStartMode2.Manual);

        act.Should().NotThrow();
        var result = act();
        result.Success.Should().BeFalse();
        result.Message.Should().Be("Service not found.");
    }

    [Fact]
    public void An_unknown_start_type_is_refused_before_anything_is_opened()
    {
        var result = ServiceProvider.SetStartMode("PowerXNoSuchService", ServiceStartMode2.Unknown);
        result.Success.Should().BeFalse();
        result.Message.Should().NotBeNullOrWhiteSpace();
    }
}
