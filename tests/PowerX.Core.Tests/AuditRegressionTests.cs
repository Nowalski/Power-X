using FluentAssertions;
using Microsoft.Win32;
using PowerX.Core.Debloat;
using PowerX.Core.Diagnostics;
using PowerX.Core.Programs;
using PowerX.Core.Startup;
using PowerX.Core.Transactions;
using PowerX.Core.Tweaks;
using Xunit;

namespace PowerX.Core.Tests;

public class ChangeLogRotationTests
{
    [Fact]
    public void Rotation_keeps_the_most_recent_lines_and_does_not_lose_the_file()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"px-rot-{Guid.NewGuid():N}.jsonl");
        var log = new ChangeLog(path);
        try
        {
            var rec = new ChangeRecord
            {
                TweakId = "t", TweakName = "t", Action = ChangeAction.Apply, Success = true,
                PreviousState = "Default", ResultingState = "Applied", Timestamp = DateTimeOffset.UtcNow,
                Message = new string('x', 600),
            };
            for (int i = 0; i < 4000; i++) log.Append(rec);   // ~3.4 MB before rotation

            var all = log.ReadAll();
            all.Should().NotBeEmpty();
            all.Count.Should().BeInRange(2000, 2900, "rotation trims to ~2000 and the file grows a little before the next trim");
            all.Count.Should().BeLessThan(4000, "without rotation it would be 4000");
            System.IO.File.Exists(path).Should().BeTrue();
        }
        finally
        {
            foreach (var p in new[] { path, path + ".tmp" })
                if (System.IO.File.Exists(p)) System.IO.File.Delete(p);
        }
    }
}

public class StartupRunOnceTests
{
    [Fact]
    public void RunOnce_cannot_be_toggled_but_can_be_removed_with_a_backup()
    {
        const string runOnce = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
        string valueName = $"PowerXTest_{Guid.NewGuid():N}";
        using (var k = Registry.CurrentUser.CreateSubKey(runOnce, writable: true))
            k.SetValue(valueName, "C:\\Windows\\System32\\cmd.exe /c echo hi", RegistryValueKind.String);

        try
        {
            var entry = new StartupEntry
            {
                Name = valueName, Command = "cmd.exe", Source = StartupSource.RunOnceUser, Enabled = true,
            };

            StartupProvider.CanToggle(entry).Should().BeFalse();
            StartupProvider.SetEnabled(entry, false).Success.Should().BeFalse();

            StartupProvider.CanRemove(entry).Should().BeTrue();
            StartupProvider.Remove(entry).Success.Should().BeTrue();

            using var check = Registry.CurrentUser.OpenSubKey(runOnce);
            check!.GetValue(valueName).Should().BeNull("the RunOnce value should be deleted");

            using var bak = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PowerX\RemovedRunOnce");
            bak!.GetValue($"HKCU\\{runOnce}\\{valueName}").Should().NotBeNull("the deleted value should be stashed for recovery");
        }
        finally
        {
            using var k = Registry.CurrentUser.OpenSubKey(runOnce, writable: true);
            k?.DeleteValue(valueName, throwOnMissingValue: false);
            using var bak = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PowerX\RemovedRunOnce", writable: true);
            bak?.DeleteValue($"HKCU\\{runOnce}\\{valueName}", throwOnMissingValue: false);
        }
    }

    [Fact]
    public void A_broken_Run_entry_is_flagged_and_can_be_removed()
    {
        const string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        string valueName = $"PowerXTest_{Guid.NewGuid():N}";
        string missingPath = $@"C:\PowerXTestMissing\{Guid.NewGuid():N}\ghost.exe";
        using (var k = Registry.CurrentUser.CreateSubKey(runKey, writable: true))
            k.SetValue(valueName, $"\"{missingPath}\" --flag", RegistryValueKind.String);

        try
        {
            var entry = StartupProvider.Enumerate().FirstOrDefault(e => e.Name == valueName);
            entry.Should().NotBeNull();
            entry!.Broken.Should().BeTrue();
            entry.ExecutablePath.Should().BeNull();

            StartupProvider.CanRemove(entry).Should().BeTrue();
            StartupProvider.Remove(entry).Success.Should().BeTrue();

            using var check = Registry.CurrentUser.OpenSubKey(runKey);
            check!.GetValue(valueName).Should().BeNull("the broken Run value should be deleted");

            using var bak = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PowerX\RemovedRunOnce");
            bak!.GetValue($"HKCU\\{runKey}\\{valueName}").Should().NotBeNull("the deleted value should be stashed for recovery");
        }
        finally
        {
            using var k = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
            k?.DeleteValue(valueName, throwOnMissingValue: false);
            using var bak = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\PowerX\RemovedRunOnce", writable: true);
            bak?.DeleteValue($"HKCU\\{runKey}\\{valueName}", throwOnMissingValue: false);
        }
    }

    [Fact]
    public void A_bare_command_name_that_does_not_resolve_is_not_flagged_broken()
    {
        const string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        string valueName = $"PowerXTest_{Guid.NewGuid():N}";
        using (var k = Registry.CurrentUser.CreateSubKey(runKey, writable: true))
            k.SetValue(valueName, "rundll32.exe shell32.dll,SomeEntryPoint", RegistryValueKind.String);

        try
        {
            var entry = StartupProvider.Enumerate().FirstOrDefault(e => e.Name == valueName);
            entry.Should().NotBeNull();
            // rundll32.exe is a bare command name (PowerX does not search PATH), so it does not
            // resolve — but it also should not be flagged as a broken/removable entry.
            entry!.Broken.Should().BeFalse();
            StartupProvider.CanRemove(entry).Should().BeFalse();
        }
        finally
        {
            using var k = Registry.CurrentUser.OpenSubKey(runKey, writable: true);
            k?.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }
}

public class UninstallCommandParsingTests
{
    [Theory]
    // quoted path with spaces + args
    [InlineData("\"C:\\Program Files\\App\\unins000.exe\" /SILENT", "C:\\Program Files\\App\\unins000.exe", "/SILENT")]
    // UNQUOTED path with spaces (the regression) — must not split mid-path
    [InlineData("C:\\Program Files\\App\\uninstall.exe /S", "C:\\Program Files\\App\\uninstall.exe", "/S")]
    // MSI product code
    [InlineData("MsiExec.exe /X{2D6D5E9C-1111-2222-3333-444455556666}", "MsiExec.exe", "/X{2D6D5E9C-1111-2222-3333-444455556666}")]
    // bare exe, no args
    [InlineData("C:\\Windows\\System32\\wusa.exe", "C:\\Windows\\System32\\wusa.exe", "")]
    // rundll32 form
    [InlineData("rundll32.exe advpack.dll,LaunchINFSection foo.inf,Uninstall", "rundll32.exe", "advpack.dll,LaunchINFSection foo.inf,Uninstall")]
    public void SplitCommand_separates_executable_from_arguments(string command, string file, string args)
    {
        var (f, a) = InstalledPrograms.SplitCommand(command);
        f.Should().Be(file);
        a.Should().Be(args);
    }
}

public class DebloatCatalogTests
{
    [Fact]
    public void Entries_are_well_formed_and_unambiguous()
    {
        var entries = DebloatCatalog.Entries;
        entries.Should().OnlyContain(e =>
            !string.IsNullOrWhiteSpace(e.FamilyNameContains) &&
            !string.IsNullOrWhiteSpace(e.DisplayName) &&
            !string.IsNullOrWhiteSpace(e.Category) &&
            !string.IsNullOrWhiteSpace(e.Description));

        // Match keys must be unique (case-insensitive) so a package resolves to one stance.
        entries.Select(e => e.FamilyNameContains.ToLowerInvariant())
            .Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void No_entry_key_is_a_substring_of_another_different_entry()
    {
        var keys = DebloatCatalog.Entries.Select(e => e.FamilyNameContains).ToList();
        foreach (var a in keys)
            foreach (var b in keys)
                if (!ReferenceEquals(a, b) && a != b)
                    b.Contains(a, StringComparison.OrdinalIgnoreCase)
                        .Should().BeFalse($"'{a}' is a substring of '{b}' — Match() would be order-dependent");
    }
}

public class AbsentValueDefaultTests
{
    [Fact]
    public void Absent_registry_value_reads_as_default_not_custom()
    {
        string keyPath = $@"Software\PowerX.Tests\{Guid.NewGuid():N}";
        // Windows default for this tweak is "value absent"; DefaultValue is the concrete 0.
        var op = new RegistryTweakOperation(
            new RegistryValueSpec(RegistryHive2.CurrentUser, keyPath, "Flag", RegistryValueKind.DWord, 1, 0));
        var ctx = TweakContext.Detect();

        try
        {
            op.Detect(ctx).Should().Be(TweakState.Default);   // key/value absent

            op.Apply(ctx).Success.Should().BeTrue();
            op.Detect(ctx).Should().Be(TweakState.Applied);

            op.Revert(ctx).Success.Should().BeTrue();
            op.Detect(ctx).Should().Be(TweakState.Default);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
    }
}

public class ProfileTests
{
    [Fact]
    public void Every_profile_tweak_id_resolves_and_is_safe()
    {
        var catalog = TweakCatalog.Default.ToDictionary(t => t.Id);
        foreach (var profile in Profiles.All)
        {
            profile.Id.Should().NotBeNullOrWhiteSpace();
            foreach (var id in profile.TweakIds)
            {
                catalog.Should().ContainKey(id, $"profile '{profile.Id}' references '{id}'");
                var t = catalog[id];
                t.Risk.Should().NotBe(TweakRisk.SecurityTradeoff, $"'{id}' in profile '{profile.Id}'");
                t.Risk.Should().NotBe(TweakRisk.Destructive, $"'{id}' in profile '{profile.Id}'");
            }
        }
    }

    [Fact]
    public void Profile_ids_are_unique()
        => Profiles.All.Select(p => p.Id).Should().OnlyHaveUniqueItems();
}

public class CleanupScannerTests
{
    [Fact]
    public void Targets_have_stable_ids_and_at_least_one_path_or_are_special()
    {
        var targets = CleanupScanner.BuildTargets();
        targets.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        targets.Should().OnlyContain(t => t.IsRecycleBin || t.Paths.Count > 0);
        targets.Should().OnlyContain(t =>
            !string.IsNullOrWhiteSpace(t.Name) && !string.IsNullOrWhiteSpace(t.Description));
    }

    [Fact]
    public void Measure_never_throws_on_the_current_machine()
    {
        var act = () =>
        {
            foreach (var t in CleanupScanner.BuildTargets()) CleanupScanner.Measure(t);
        };
        act.Should().NotThrow();
    }
}
