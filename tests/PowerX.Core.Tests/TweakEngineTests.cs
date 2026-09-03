using FluentAssertions;
using Microsoft.Win32;
using PowerX.Core.Transactions;
using PowerX.Core.Tweaks;
using Xunit;

namespace PowerX.Core.Tests;

public class TweakEngineTests
{
    private static TweakDefinition SampleTweak(RegistryValueSpec spec) => new()
    {
        Id = "test.sample",
        Name = "Sample",
        Category = "Test",
        WhatItDoes = "x",
        WhyYouMightWant = "y",
        Downside = "z",
        Risk = TweakRisk.Low,
        Operation = new RegistryTweakOperation(spec),
    };

    private static (TweakEngine engine, string keyPath, ChangeLog log) NewEngine(out string valueName)
    {
        string keyPath = $@"Software\PowerX.Tests\{Guid.NewGuid():N}";
        valueName = "Flag";
        var spec = new RegistryValueSpec(RegistryHive2.CurrentUser, keyPath, "Flag", RegistryValueKind.DWord, 1, 0);
        var log = new ChangeLog(Path.Combine(Path.GetTempPath(), $"px-{Guid.NewGuid():N}.jsonl"));
        var engine = new TweakEngine([SampleTweak(spec)], log);
        return (engine, keyPath, log);
    }

    [Fact]
    public void Apply_then_revert_round_trips_state()
    {
        var (engine, keyPath, _) = NewEngine(out _);
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(keyPath)) k.SetValue("Flag", 0, RegistryValueKind.DWord);

            engine.GetStatus("test.sample").State.Should().Be(TweakState.Default);

            var apply = engine.Execute("test.sample", ChangeAction.Apply);
            apply.Success.Should().BeTrue();
            engine.GetStatus("test.sample").State.Should().Be(TweakState.Applied);

            var revert = engine.Execute("test.sample", ChangeAction.Revert);
            revert.Success.Should().BeTrue();
            engine.GetStatus("test.sample").State.Should().Be(TweakState.Default);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void Apply_is_idempotent_and_reports_no_change()
    {
        var (engine, keyPath, _) = NewEngine(out _);
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(keyPath)) k.SetValue("Flag", 0, RegistryValueKind.DWord);
            engine.Execute("test.sample", ChangeAction.Apply);
            var second = engine.Execute("test.sample", ChangeAction.Apply);
            second.Success.Should().BeTrue();
            second.PreviousState.Should().Be(second.ResultingState);
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void DryRun_does_not_write_or_log()
    {
        var (engine, keyPath, log) = NewEngine(out _);
        try
        {
            using (var k = Registry.CurrentUser.CreateSubKey(keyPath)) k.SetValue("Flag", 0, RegistryValueKind.DWord);
            var ctx = TweakContext.Detect() with { DryRun = true };
            engine.Execute("test.sample", ChangeAction.Apply, ctx);

            engine.GetStatus("test.sample").State.Should().Be(TweakState.Default);
            log.ReadAll().Should().BeEmpty();
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
    }

    [Fact]
    public void Unsupported_build_is_not_applicable()
    {
        var (engine, _, _) = NewEngine(out _);
        var future = TweakContext.Detect() with { WindowsBuild = 1 };
        var def = engine.Find("test.sample")! with { MinBuild = 99_999 };
        var engine2 = new TweakEngine([def]);
        engine2.GetStatus("test.sample", future).State.Should().Be(TweakState.NotApplicable);
    }

    [Fact]
    public void Absent_registry_value_reads_as_default_not_unknown()
    {
        // A value that simply does not exist is "Windows default", never an error.
        var spec = new RegistryValueSpec(RegistryHive2.CurrentUser,
            $@"Software\PowerX.Tests\{Guid.NewGuid():N}", "Missing", RegistryValueKind.DWord, 1, 0);
        var engine = new TweakEngine([SampleTweak(spec)]);
        engine.GetStatus("test.sample").State.Should().Be(TweakState.Default);
    }

    [Fact]
    public void Detect_that_throws_surfaces_as_unknown_and_execute_does_not_crash()
    {
        // A registry read that fails on ACL (elevation) must not be reported as "Default".
        var log = new ChangeLog(Path.Combine(Path.GetTempPath(), $"px-{Guid.NewGuid():N}.jsonl"));
        var engine = new TweakEngine([SampleTweak(new RegistryValueSpec(
            RegistryHive2.CurrentUser, "x", "x", RegistryValueKind.DWord, 1, 0)) with
        {
            Id = "test.throws",
            Operation = new ThrowingOperation(),
        }], log);

        engine.GetStatus("test.throws").State.Should().Be(TweakState.Unknown);

        var rec = engine.Execute("test.throws", ChangeAction.Apply);
        rec.Success.Should().BeFalse();
        rec.PreviousState.Should().Be(nameof(TweakState.Unknown));
    }

    private sealed class ThrowingOperation : ITweakOperation
    {
        public TweakState Detect(TweakContext c) => throw new InvalidOperationException("Could not read HKLM\\... Elevation may be required.");
        public TweakOutcome Apply(TweakContext c) => TweakOutcome.Fail("nope");
        public TweakOutcome Revert(TweakContext c) => TweakOutcome.Fail("nope");
        public bool Verify(TweakContext c) => false;
    }

    [Fact]
    public void RevertableChanges_excludes_reverted_failed_and_noop_applies()
    {
        var path = Path.Combine(Path.GetTempPath(), $"px-{Guid.NewGuid():N}.jsonl");
        var log = new ChangeLog(path);
        try
        {
            ChangeRecord Rec(string id, ChangeAction a, bool ok, string prev, string result) => new()
            {
                TweakId = id, TweakName = id, Action = a, Success = ok,
                PreviousState = prev, ResultingState = result, Timestamp = DateTimeOffset.UtcNow,
            };

            log.Append(Rec("a.applied", ChangeAction.Apply, true, "Default", "Applied"));     // revertable
            log.Append(Rec("b.then-reverted", ChangeAction.Apply, true, "Default", "Applied"));
            log.Append(Rec("b.then-reverted", ChangeAction.Revert, true, "Applied", "Default")); // not revertable
            log.Append(Rec("c.apply-failed", ChangeAction.Apply, false, "Default", "Unknown")); // not revertable
            log.Append(Rec("d.ended-custom", ChangeAction.Apply, true, "Default", "Custom"));   // not revertable

            var ids = log.RevertableChanges().Select(r => r.TweakId).ToList();
            ids.Should().BeEquivalentTo(["a.applied"]);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
