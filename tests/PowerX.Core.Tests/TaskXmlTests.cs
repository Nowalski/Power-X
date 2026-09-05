using FluentAssertions;
using PowerX.Core.Startup;
using Xunit;

namespace PowerX.Core.Tests;

public class TaskXmlTests
{
    private const string Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

    private static string Task(string body) =>
        $"""<?xml version="1.0" encoding="UTF-16"?><Task version="1.2" xmlns="{Ns}">{body}</Task>""";

    [Fact]
    public void Reads_author_description_action_and_hidden()
    {
        var p = TaskXml.Parse(Task("""
            <RegistrationInfo><Author>Contoso Ltd</Author><Description>Keeps things tidy</Description></RegistrationInfo>
            <Settings><Hidden>true</Hidden></Settings>
            <Actions><Exec><Command>C:\App\tidy.exe</Command><Arguments>--now</Arguments></Exec></Actions>
            """));

        p.Should().NotBeNull();
        p!.Author.Should().Be("Contoso Ltd");
        p.Description.Should().Be("Keeps things tidy");
        p.Hidden.Should().BeTrue();
        p.Action.Should().Be(@"C:\App\tidy.exe --now");
    }

    [Theory]
    [InlineData("<LogonTrigger />", "at logon", true)]
    [InlineData("<BootTrigger />", "at boot", true)]
    [InlineData("<IdleTrigger />", "on idle", false)]
    [InlineData("<EventTrigger />", "on an event", false)]
    [InlineData("<TimeTrigger />", "once", false)]
    [InlineData("<RegistrationTrigger />", "at registration", false)]
    [InlineData("<SessionStateChangeTrigger />", "on session change", false)]
    [InlineData("<CalendarTrigger><ScheduleByDay /></CalendarTrigger>", "daily", false)]
    [InlineData("<CalendarTrigger><ScheduleByWeek /></CalendarTrigger>", "weekly", false)]
    [InlineData("<CalendarTrigger><ScheduleByMonth /></CalendarTrigger>", "monthly", false)]
    [InlineData("<CalendarTrigger><ScheduleByMonthDayOfWeek /></CalendarTrigger>", "monthly (day of week)", false)]
    public void Maps_each_trigger_to_the_wording_the_object_model_used(string trigger, string expected, bool isStartup)
    {
        var p = TaskXml.Parse(Task($"<Triggers>{trigger}</Triggers>"));

        p.Should().NotBeNull();
        p!.Triggers.Should().Be(expected);
        p.IsLogonOrBoot.Should().Be(isStartup);
    }

    [Fact]
    public void Collapses_repeated_trigger_kinds_and_keeps_order()
    {
        var p = TaskXml.Parse(Task("""
            <Triggers><LogonTrigger /><LogonTrigger /><CalendarTrigger><ScheduleByDay /></CalendarTrigger></Triggers>
            """));

        p!.Triggers.Should().Be("at logon, daily");
        p.IsLogonOrBoot.Should().BeTrue();
    }

    [Fact]
    public void A_com_handler_first_action_shows_nothing_like_the_object_model_did()
    {
        var p = TaskXml.Parse(Task("""
            <Actions><ComHandler><ClassId>{00000000-0000-0000-0000-000000000000}</ClassId></ComHandler>
            <Exec><Command>ignored.exe</Command></Exec></Actions>
            """));

        p!.Action.Should().BeEmpty();
    }

    [Fact]
    public void A_task_with_no_optional_sections_parses_to_empties_rather_than_throwing()
    {
        var p = TaskXml.Parse(Task(""));

        p.Should().NotBeNull();
        p!.Author.Should().BeEmpty();
        p.Description.Should().BeEmpty();
        p.Action.Should().BeEmpty();
        p.Triggers.Should().BeEmpty();
        p.Hidden.Should().BeFalse();
        p.IsLogonOrBoot.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not xml at all")]
    [InlineData("<Task><unclosed>")]
    public void Unreadable_xml_returns_null_so_the_caller_skips_that_task(string? xml)
    {
        TaskXml.Parse(xml).Should().BeNull();
    }

    [Fact]
    public void A_literal_author_is_left_exactly_as_written()
    {
        // Only "$(@file,-id)" MUI references get resolved; anything else must pass through
        // untouched, including text that merely looks similar.
        var p = TaskXml.Parse(Task("<RegistrationInfo><Author>$(not an indirect string</Author></RegistrationInfo>"));

        p!.Author.Should().Be("$(not an indirect string");
    }

    [Fact]
    public void An_unresolvable_mui_reference_falls_back_to_the_raw_string_rather_than_emptying_it()
    {
        var p = TaskXml.Parse(Task(
            @"<RegistrationInfo><Author>$(@%SystemRoot%\system32\powerx-does-not-exist.dll,-1)</Author></RegistrationInfo>"));

        p!.Author.Should().Be(@"$(@%SystemRoot%\system32\powerx-does-not-exist.dll,-1)");
    }
}
