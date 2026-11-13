using FluentAssertions;
using PowerX.Core.Diagnostics;
using Xunit;

namespace PowerX.Core.Tests;

public class RepairJobCatalogTests
{
    [Fact]
    public void Every_job_either_runs_steps_or_drives_itself()
    {
        foreach (var job in CommandRunner.Jobs)
            (job.Steps.Count > 0 || job.Custom is not null).Should().BeTrue(
                "'{0}' would run nothing at all", job.Title);
    }

    [Fact]
    public void Every_step_names_an_executable()
    {
        foreach (var job in CommandRunner.Jobs)
            foreach (var step in job.Steps)
                step.File.Should().NotBeNullOrWhiteSpace("'{0}' has a step with no program", job.Title);
    }

    /// <summary>
    /// The regression this file exists for. "Generate a system diagnostics report" used to be
    /// the step perfmon.exe /report. perfmon is a GUI program: it opened the Resource and
    /// Performance Monitor window, wrote nothing to the streams PowerX captures, and did not
    /// exit until the window was closed. The Repair console therefore showed the job header and
    /// then nothing at all, and no report was ever handed back to open.
    /// </summary>
    [Fact]
    public void No_job_shells_out_to_a_gui_program_that_cannot_stream()
    {
        foreach (var job in CommandRunner.Jobs)
            foreach (var step in job.Steps)
                step.File.Contains("perfmon", StringComparison.OrdinalIgnoreCase).Should().BeFalse(
                    "'{0}' would open a window and stream nothing", job.Title);
    }

    /// <summary>
    /// RepairPage's "Recommended repair" button finds these two jobs by matching their titles.
    /// Renaming either one used to throw out of an async void click handler; this pins the
    /// contract so a rename fails here instead of at the user's fingertip.
    /// </summary>
    [Fact]
    public void The_recommended_repair_sequence_can_still_find_its_two_jobs()
    {
        CommandRunner.Jobs.Should().ContainSingle(j => j.Title.Contains("RestoreHealth", StringComparison.Ordinal));
        CommandRunner.Jobs.Should().ContainSingle(j => j.Title.StartsWith("Scan & repair", StringComparison.Ordinal));
    }

    [Fact]
    public void The_system_diagnostics_job_drives_the_collector_itself()
    {
        var job = CommandRunner.Jobs.Single(j => j.Title.Contains("system diagnostics", StringComparison.OrdinalIgnoreCase));
        job.Custom.Should().NotBeNull();
        job.Steps.Should().BeEmpty();
        job.Destructive.Should().BeFalse("collecting diagnostics only reads state");
    }
}

/// <summary>
/// The freshness rule that <see cref="SystemDiagnosticsReport"/> applies to report.html.
///
/// PLA's COM properties are a snapshot taken by Query rather than live values, so the first
/// version of that class read a stale status and a stale output folder, found the *previous*
/// run's report.html sitting there, and opened it as if it were the new one. Nothing failed and
/// nothing looked wrong: the console said "Report ready" and a real report appeared. Only
/// comparing the reported path against the folder the run had actually just written showed it.
/// A report older than the moment collection started belongs to an earlier run.
/// </summary>
public class ReportFreshnessTests
{
    private static bool IsFresh(string path, DateTime startedAtUtc)
        => CommandRunner.IsFresh(path, startedAtUtc);

    [Fact]
    public void A_report_written_after_collection_started_is_accepted()
    {
        string path = Path.Combine(Path.GetTempPath(), $"powerx-fresh-{Guid.NewGuid():N}.html");
        var startedAt = DateTime.UtcNow.AddSeconds(-5);
        File.WriteAllText(path, "report");
        try { IsFresh(path, startedAt).Should().BeTrue(); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_report_left_over_from_an_earlier_run_is_rejected()
    {
        string path = Path.Combine(Path.GetTempPath(), $"powerx-stale-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, "report from the previous run");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));
        try { IsFresh(path, DateTime.UtcNow).Should().BeFalse(); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void A_missing_report_is_rejected()
        => IsFresh(Path.Combine(Path.GetTempPath(), $"powerx-none-{Guid.NewGuid():N}.html"),
                   DateTime.UtcNow).Should().BeFalse();
}

public class JobRunPlumbingTests
{
    [Fact]
    public async Task A_self_driving_job_returns_the_report_it_found_at_run_time()
    {
        var job = new CommandRunner.Job("Reports", "fake", "fake",
            (onLine, _) =>
            {
                onLine("working");
                return Task.FromResult(new CommandRunner.JobRun(0, @"C:\somewhere\report.html"));
            });

        var lines = new List<string>();
        var run = await CommandRunner.RunAsync(job, lines.Add);

        run.ExitCode.Should().Be(0);
        run.ReportPath.Should().Be(@"C:\somewhere\report.html");
        lines.Should().ContainSingle().Which.Should().Be("working");
    }

    [Fact]
    public async Task A_step_job_reports_a_report_it_actually_wrote()
    {
        string path = Path.Combine(Path.GetTempPath(), $"powerx-job-{Guid.NewGuid():N}.html");
        var job = new CommandRunner.Job("Reports", "fake", "fake",
            "cmd.exe", $"/c echo report> \"{path}\"", openReport: path);

        try
        {
            var run = await CommandRunner.RunAsync(job, _ => { });
            run.ExitCode.Should().Be(0);
            run.ReportPath.Should().Be(path);
        }
        finally { File.Delete(path); }
    }

    /// <summary>
    /// powercfg /batteryreport exits 0 and prints "Battery life report saved to ..." even when the
    /// output path was not writable and nothing was written. The report path lives in %TEMP% and
    /// survives between runs, so a run that quietly wrote nothing would otherwise hand back the
    /// previous run's file and PowerX would open stale data under a success message.
    /// </summary>
    [Fact]
    public async Task A_step_job_does_not_offer_a_report_left_over_from_a_previous_run()
    {
        string path = Path.Combine(Path.GetTempPath(), $"powerx-stalejob-{Guid.NewGuid():N}.html");
        File.WriteAllText(path, "the previous run's report");
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-2));

        // exits 0, prints success, writes nothing: exactly what powercfg does on an unwritable path
        var job = new CommandRunner.Job("Reports", "fake", "fake",
            "cmd.exe", "/c echo Report saved.", openReport: path);

        try
        {
            var lines = new List<string>();
            var run = await CommandRunner.RunAsync(job, lines.Add);

            run.ExitCode.Should().Be(0);
            run.ReportPath.Should().BeNull("the file predates this run");
            lines.Should().Contain(l => l.Contains("nothing to open", StringComparison.Ordinal));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task A_step_job_does_not_offer_a_report_that_was_never_written()
    {
        string path = Path.Combine(Path.GetTempPath(), $"powerx-never-{Guid.NewGuid():N}.html");
        var job = new CommandRunner.Job("Reports", "fake", "fake",
            "cmd.exe", "/c echo Report saved.", openReport: path);

        var run = await CommandRunner.RunAsync(job, _ => { });

        run.ExitCode.Should().Be(0);
        run.ReportPath.Should().BeNull();
    }

    [Fact]
    public async Task A_step_job_surfaces_a_non_zero_exit_code()
    {
        var job = new CommandRunner.Job("Reports", "fake", "fake", "cmd.exe", "/c exit 3");

        var run = await CommandRunner.RunAsync(job, _ => { });

        run.ExitCode.Should().Be(3);
        run.ReportPath.Should().BeNull();
    }

    [Fact]
    public async Task Cancellation_reaches_a_self_driving_job()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var job = new CommandRunner.Job("Reports", "fake", "fake",
            async (onLine, ct) =>
            {
                try { await Task.Delay(TimeSpan.FromMinutes(1), ct); }
                catch (OperationCanceledException) { onLine("cancelled"); return new CommandRunner.JobRun(-1, null); }
                return new CommandRunner.JobRun(0, null);
            });

        var lines = new List<string>();
        var run = await CommandRunner.RunAsync(job, lines.Add, cts.Token);

        run.ExitCode.Should().Be(-1);
        lines.Should().Contain("cancelled");
    }
}
