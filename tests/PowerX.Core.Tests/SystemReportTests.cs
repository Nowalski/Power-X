using FluentAssertions;
using PowerX.Core.Diagnostics;
using Xunit;

namespace PowerX.Core.Tests;

public class SystemReportTests
{
    [Fact]
    public void BuildMarkdown_produces_every_section()
    {
        var md = SystemReport.BuildMarkdown(new ReportOptions { EventWindow = TimeSpan.FromDays(1), ChangeHistoryCount = 5 });

        md.Should().StartWith("# PowerX system report");
        foreach (var h in new[] { "## System", "## Hardware", "## Storage", "## Applied tweaks", "## Recent changes", "## Event-log errors", "## Crashes" })
            md.Should().Contain(h);
    }

    [Fact]
    public void BuildMarkdown_redacts_the_user_and_machine_name_by_default()
    {
        var md = SystemReport.BuildMarkdown(new ReportOptions { EventWindow = TimeSpan.FromDays(1) });

        string user = Environment.UserName;
        string machine = Environment.MachineName;
        if (user.Length >= 4)
            md.Should().NotContain(user, "the user name must be scrubbed from a redacted report");
        if (machine.Length >= 4)
            md.Should().NotContain(machine);
    }

    [Fact]
    public void BuildMarkdown_without_redaction_names_the_machine()
    {
        var md = SystemReport.BuildMarkdown(new ReportOptions { Redact = false, EventWindow = TimeSpan.FromDays(1) });
        md.Should().Contain(Environment.MachineName);
        md.Should().NotContain("identifiers are redacted");
    }
}
