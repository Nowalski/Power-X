using FluentAssertions;
using PowerX.Core.Diagnostics;
using PowerX.Core.Startup;
using PowerX.Core.Transactions;
using Xunit;

namespace PowerX.Core.Tests;

public class FeatureCommandsTests
{
    [Fact]
    public void DriverInventory_never_throws()
    {
        var act = () => DriverInventory.Read();
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData(0, DriverAge.Unknown)]
    [InlineData(1, DriverAge.Current)]
    [InlineData(3, DriverAge.Old)]
    [InlineData(6, DriverAge.VeryOld)]
    public void DriverEntry_flags_age(int years, DriverAge expected)
    {
        var d = new DriverEntry
        {
            Device = "x", Version = "1", Provider = "Acme",
            Date = years == 0 ? null : DateTimeOffset.Now.AddYears(-years).AddDays(-5),
        };
        d.Age.Should().Be(expected);
    }

    [Fact]
    public void DriverEntry_never_flags_a_microsoft_inbox_driver()
    {
        var d = new DriverEntry
        {
            Device = "Standard SATA AHCI Controller", Version = "10.0", Provider = "Microsoft",
            Date = DateTimeOffset.Now.AddYears(-9),
        };
        d.Age.Should().Be(DriverAge.Current);
    }

    [Theory]
    [InlineData(@"\Microsoft\Windows\Customer Experience Improvement Program\Consolidator", TaskStance.Telemetry)]
    [InlineData(@"\Microsoft\Windows\UpdateOrchestrator\Schedule Scan", TaskStance.KeepSystem)]
    [InlineData(@"\GoogleUpdateTaskMachineUA", TaskStance.Optional)]
    [InlineData(@"\Custom\MyBackup", TaskStance.Unreviewed)]
    public void ScheduledTaskCatalog_takes_a_position_on_known_tasks(string path, TaskStance expected)
    {
        ScheduledTaskCatalog.StanceFor(path, out var note).Should().Be(expected);
        if (expected != TaskStance.Unreviewed) note.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void FirewallRule_flags_a_broad_open_port_but_not_a_store_app()
    {
        var openPort = new FirewallRule
        {
            Name = "Open TCP Port 30303", Direction = FwDirection.In, Action = FwAction.Allow,
            Enabled = true, Public = true, LocalPorts = "30303", Protocol = "TCP",
        };
        openPort.WorthReviewing.Should().BeTrue();

        var storeApp = openPort with { Name = "Game Bar", LocalPorts = "", Owner = "S-1-5-21-1-2-3-1001" };
        storeApp.WorthReviewing.Should().BeFalse();
    }

    [Fact]
    public void EventLogBrowser_never_throws()
    {
        var act = () => EventLogBrowser.Read(TimeSpan.FromDays(1), includeWarnings: false);
        act.Should().NotThrow();
    }

    [Fact]
    public void ConfigBundle_round_trips_through_json()
    {
        var b = ConfigBundleService.Export();
        var back = ConfigBundleService.FromJson(ConfigBundleService.ToJson(b));
        back.Should().NotBeNull();
        back!.AppliedTweaks.Should().BeEquivalentTo(b.AppliedTweaks);
        back.Schema.Should().StartWith("powerx.config/");
    }

    [Fact]
    public void ConfigBundle_rejects_a_file_that_is_not_ours()
    {
        ConfigBundleService.FromJson("""{"hello":"world"}""").Should().BeNull();
        ConfigBundleService.FromJson("not json at all").Should().BeNull();
    }

    [Fact]
    public void ConfigBundle_plan_skips_unknown_tweak_ids_with_a_warning()
    {
        var bundle = new ConfigBundle { AppliedTweaks = ["this.tweak.does-not-exist"] };
        var plan = ConfigBundleService.Plan(bundle, []);
        plan.TweaksToApply.Should().BeEmpty();
        plan.Warnings.Should().ContainMatch("*this.tweak.does-not-exist*");
    }
}
