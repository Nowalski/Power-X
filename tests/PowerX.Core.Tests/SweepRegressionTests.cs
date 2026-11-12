using FluentAssertions;
using Microsoft.Win32;
using PowerX.Core.Diagnostics;
using PowerX.Core.Transactions;
using Xunit;

namespace PowerX.Core.Tests;

public class CleanupContainmentTests
{
    private const string Root = @"\\?\C:\Windows\Temp\";

    [Theory]
    [InlineData(@"\\?\C:\Windows\Temp\a.tmp")]
    [InlineData(@"\\?\C:\Windows\Temp\sub\b.log")]
    [InlineData(@"\\?\c:\windows\temp\CASE.tmp")]
    public void Items_inside_the_root_are_contained(string final)
        => CleanupScanner.IsContained(final, Root).Should().BeTrue();

    [Theory]
    [InlineData(@"\\?\C:\Windows\System32\kernel32.dll")]   // a junction swapped in mid-walk
    [InlineData(@"\\?\C:\Windows\Temp")]                    // the root itself
    [InlineData(@"\\?\C:\Windows\Temp\")]
    [InlineData(@"\\?\C:\Windows\TempEvil\x.dll")]          // sibling sharing the prefix
    public void Items_outside_the_root_are_not_contained(string final)
        => CleanupScanner.IsContained(final, Root).Should().BeFalse();

    [Fact]
    public void Root_gets_exactly_one_trailing_separator()
    {
        CleanupScanner.WithTrailingSeparator(@"\\?\C:\Windows\Temp").Should().Be(Root);
        CleanupScanner.WithTrailingSeparator(Root).Should().Be(Root);
    }

    [Fact]
    public void Kernel_dump_target_no_longer_points_into_the_user_profile()
    {
        var t = CleanupScanner.BuildTargets().Single(x => x.Id == "kernel-dumps");
        t.Paths.Should().NotContain(p => p.Contains("SystemTemp", StringComparison.OrdinalIgnoreCase));
        t.Paths.Should().Contain(p => p.EndsWith("MEMORY.DMP", StringComparison.OrdinalIgnoreCase));
    }
}

public class EnvironmentVariableKindTests
{
    [Fact]
    public void An_existing_expandable_value_stays_expandable()
        => EnvironmentVariables.KindFor(RegistryValueKind.ExpandString, @"C:\Tools").Should().Be(RegistryValueKind.ExpandString);

    [Fact]
    public void A_value_that_references_a_variable_becomes_expandable()
        => EnvironmentVariables.KindFor(null, @"%USERPROFILE%\bin").Should().Be(RegistryValueKind.ExpandString);

    [Theory]
    [InlineData("plain value")]
    [InlineData("100%")]
    [InlineData("%%")]
    public void A_plain_value_stays_a_plain_string(string value)
        => EnvironmentVariables.KindFor(RegistryValueKind.String, value).Should().Be(RegistryValueKind.String);
}

public class ConfigBundleImportSafetyTests
{
    [Fact]
    public void Plan_only_offers_to_remove_curated_debloat_apps()
    {
        var bundle = new ConfigBundle
        {
            Schema = ConfigBundle.CurrentSchema,
            RemovedApps = ["", "   ", "Microsoft.WindowsStore", "Microsoft.BingNews"],
        };
        var installed = new List<InstalledAppLite>
        {
            new("Microsoft Store", "Microsoft.WindowsStore_8wekyb3d8bbwe"),
            new("Microsoft News", "Microsoft.BingNews_8wekyb3d8bbwe"),
        };

        var plan = ConfigBundleService.Plan(bundle, installed);

        plan.AppsToRemove.Select(a => a.Id).Should().Equal("Microsoft.BingNews_8wekyb3d8bbwe");
        plan.Warnings.Should().Contain(w => w.Contains("Microsoft.WindowsStore"));
    }

    [Fact]
    public void A_bundle_from_a_newer_PowerX_is_flagged()
    {
        var current = new Version(0, 1, 22, 0);
        ConfigBundleService.NewerThanThisBuild(
            new ConfigBundle { Schema = ConfigBundle.CurrentSchema, PowerXVersion = "0.2.0" }, current)
            .Should().Contain("0.2.0");
        ConfigBundleService.NewerThanThisBuild(
            new ConfigBundle { Schema = "powerx.config/2", PowerXVersion = "0.1.0" }, current)
            .Should().Contain("powerx.config/2");
    }

    [Fact]
    public void A_bundle_from_this_or_an_older_PowerX_is_not_flagged()
    {
        var current = new Version(0, 1, 22, 0);
        ConfigBundleService.NewerThanThisBuild(
            new ConfigBundle { Schema = ConfigBundle.CurrentSchema, PowerXVersion = "0.1.22" }, current).Should().BeNull();
        ConfigBundleService.NewerThanThisBuild(
            new ConfigBundle { Schema = ConfigBundle.CurrentSchema, PowerXVersion = "" }, current).Should().BeNull();
    }
}
