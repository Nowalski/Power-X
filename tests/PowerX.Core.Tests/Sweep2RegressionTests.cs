using System.Text;
using FluentAssertions;
using PowerX.Core.Diagnostics;
using PowerX.Core.Processes;
using PowerX.Core.Programs;
using Xunit;

namespace PowerX.Core.Tests;

public class PowerPlanParsingTests
{
    private const string English = """

        Existing Power Schemes (* Active)
        -----------------------------------
        Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *
        Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)
        Power Scheme GUID: a1841308-3541-4fab-bc81-f71556f20b4a  (Power saver)
        """;

    // Windows in German: the label is translated, which the old English-only pattern missed.
    private const string German = "Vorhandene Energieschemas (* Aktiv)\r\n" +
        "-----------------------------------\r\n" +
        "GUID des Energieschemas: 381b4222-f694-41f0-9685-ff5bb260df2e  (Ausbalanciert)\r\n" +
        "GUID des Energieschemas: 6f2c9b7e-41a7-4a63-9d2e-7c1f0e5b8a31  (Ultimative Leistung) *\r\n";

    [Fact]
    public void English_output_lists_every_plan_and_the_active_one()
    {
        var plans = PowerPlans.Parse(English);
        plans.Should().HaveCount(3);
        plans.Single(p => p.Active).Id.Should().Be(PowerPlans.Balanced);
        plans.Single(p => p.Id == PowerPlans.HighPerformance).Name.Should().Be("High performance");
    }

    [Fact]
    public void Translated_output_is_parsed_too()
    {
        var plans = PowerPlans.Parse(German);
        plans.Should().HaveCount(2);
        plans.Single(p => p.Active).Name.Should().Be("Ultimative Leistung");
    }

    [Fact]
    public void The_plan_PowerX_created_is_recognised_as_Ultimate_on_any_language()
    {
        var plans = PowerPlans.Parse(German);
        plans.Count(PowerPlans.IsUltimate).Should().Be(1);
        PowerPlans.IsUltimate(new PowerPlan(Guid.NewGuid(), "Ultimate Performance", false)).Should().BeTrue();
        PowerPlans.IsUltimate(new PowerPlan(PowerPlans.Balanced, "Balanced", true)).Should().BeFalse();
    }

    [Fact]
    public void A_plan_name_with_parentheses_is_kept_whole()
    {
        var plans = PowerPlans.Parse("Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced (OEM)) *");
        plans.Single().Name.Should().Be("Balanced (OEM)");
        plans.Single().Active.Should().BeTrue();
    }
}

public class DismSizeParsingTests
{
    [Theory]
    [InlineData("7.95", 7.95)]
    [InlineData("7,95", 7.95)]           // regional decimal comma
    [InlineData("1,234.5", 1234.5)]
    [InlineData("1.234,5", 1234.5)]
    [InlineData("1,234,567", 1234567)]
    [InlineData("512", 512)]
    public void Sizes_parse_in_either_number_format(string raw, double expected)
    {
        ComponentStore.TryParseSize(raw, out var v).Should().BeTrue();
        v.Should().BeApproximately(expected, 0.0001);
    }

    [Fact]
    public void A_decimal_comma_size_is_not_read_as_a_hundred_times_bigger()
    {
        var info = ComponentStore.Parse("Actual Size of Component Store : 7,95 GB");
        info.ActualSizeBytes.Should().Be((long)(7.95 * 1024 * 1024 * 1024));
    }
}

public class EnvironmentEditorTextTests
{
    [Fact]
    public void Path_is_edited_one_entry_per_line_and_saved_back_with_semicolons()
    {
        const string path = @"%SystemRoot%\system32;%SystemRoot%;C:\Tools;";
        EnvironmentVariables.IsListVariable("Path", path).Should().BeTrue();
        string text = EnvironmentVariables.ToEditorText("Path", path);
        text.Should().Be(string.Join(Environment.NewLine, @"%SystemRoot%\system32", "%SystemRoot%", @"C:\Tools"));
        EnvironmentVariables.FromEditorText(text).Should().Be(@"%SystemRoot%\system32;%SystemRoot%;C:\Tools");
    }

    [Fact]
    public void A_line_break_never_reaches_the_stored_value()
    {
        EnvironmentVariables.FromEditorText("a\r\n\r\n b \nc").Should().Be("a;b;c");
        EnvironmentVariables.FromEditorText("plain value").Should().Be("plain value");
    }

    [Fact]
    public void Ordinary_variables_are_left_on_one_line()
    {
        EnvironmentVariables.IsListVariable("TEMP", @"C:\Temp").Should().BeFalse();
        EnvironmentVariables.IsListVariable("MYPATH", @"C:\one").Should().BeFalse();   // no list in it
        EnvironmentVariables.ToEditorText("JAVA_OPTS", "-Xmx1g;-Xms1g").Should().Be("-Xmx1g;-Xms1g");
    }
}

public class CleanupDefaultsTests
{
    [Theory]
    [InlineData("crash-dumps")]
    [InlineData("kernel-dumps")]
    public void Crash_evidence_is_not_ticked_by_default(string id)
        => CleanupScanner.BuildTargets().Single(t => t.Id == id).RecommendedDefault.Should().BeFalse();
}

public class ShellLauncherTests
{
    [Fact]
    public void The_PowerShell_hand_off_carries_the_command_safely_encoded()
    {
        string args = ShellLauncher.PowerShellStartArgs(@"C:\Users\x\AppData\Local\App's\unins.exe", "/S & calc");
        args.Should().StartWith("-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand ");
        string script = Encoding.Unicode.GetString(Convert.FromBase64String(args.Split(' ')[^1]));
        script.Should().Be(@"Start-Process -FilePath 'C:\Users\x\AppData\Local\App''s\unins.exe' -ArgumentList '/S & calc'");
    }

    [Theory]
    [InlineData(@"C:\a b\c.exe", "\"C:\\a b\\c.exe\"")]
    [InlineData("\"C:\\a b\\c.exe\"", "\"C:\\a b\\c.exe\"")]
    [InlineData("https://x.test/?q=\"y\"", "\"https://x.test/?q=y\"")]
    public void Quote_wraps_once(string input, string expected) => ShellLauncher.Quote(input).Should().Be(expected);

    [Fact]
    public void Only_HKCU_entries_count_as_per_user()
    {
        new InstalledProgram { Name = "a", UninstallCommand = "x", Scope = "user" }.IsPerUser.Should().BeTrue();
        new InstalledProgram { Name = "a", UninstallCommand = "x", Scope = "machine" }.IsPerUser.Should().BeFalse();
        new InstalledProgram { Name = "a", UninstallCommand = "x", Scope = "machine (32-bit)" }.IsPerUser.Should().BeFalse();
    }
}
