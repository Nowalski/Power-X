using System.Text.RegularExpressions;
using PowerX.Core.Processes;

namespace PowerX.Core.Diagnostics;

public sealed record PowerPlan(Guid Id, string Name, bool Active);

/// <summary>
/// Thin wrapper over <c>powercfg</c> for listing / switching power plans and creating the
/// hidden "Ultimate Performance" plan. No BCD edits, no undocumented tweaks.
/// </summary>
public static partial class PowerPlans
{
    // Well-known scheme GUIDs (documented).
    public static readonly Guid Balanced = new("381b4222-f694-41f0-9685-ff5bb260df2e");
    public static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    public static readonly Guid PowerSaver = new("a1841308-3541-4fab-bc81-f71556f20b4a");
    public static readonly Guid UltimatePerformanceTemplate = new("e9a42b02-d5df-448d-aa00-03f14749eb61");

    /// <summary>
    /// The id PowerX gives the Ultimate Performance plan it creates. Using a fixed id (instead
    /// of whatever <c>powercfg -duplicatescheme</c> picks) lets PowerX find that plan again on
    /// any display language, so a second click does not make a second copy.
    /// </summary>
    public static readonly Guid PowerXUltimatePerformance = new("6f2c9b7e-41a7-4a63-9d2e-7c1f0e5b8a31");

    // One plan per line: "<label>: <GUID>  (<name>) *". The label is translated on non-English
    // Windows ("GUID des Energieschemas", ...), so only the GUID, the name in parentheses and
    // the trailing active marker are matched.
    [GeneratedRegex(@"([0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12})\s*\((.*)\)\s*(\*)?\s*$", RegexOptions.Multiline)]
    private static partial Regex PlanLine();

    public static IReadOnlyList<PowerPlan> List()
    {
        var (ok, output) = Run("/list");
        return ok ? Parse(output) : [];
    }

    internal static IReadOnlyList<PowerPlan> Parse(string output)
    {
        var plans = new List<PowerPlan>();
        foreach (Match m in PlanLine().Matches(output.Replace("\r", "")))
        {
            if (Guid.TryParse(m.Groups[1].Value, out var id) && plans.All(p => p.Id != id))
                plans.Add(new PowerPlan(id, m.Groups[2].Value.Trim(), m.Groups[3].Success));
        }
        return plans;
    }

    public static ActionResult Activate(Guid planId)
    {
        var (ok, output) = Run($"/setactive {planId:D}");
        return ok ? ActionResult.Ok : ActionResult.Fail(output.Trim());
    }

    /// <summary>Is this the Ultimate Performance plan (by id, or by its English name)?</summary>
    internal static bool IsUltimate(PowerPlan p) =>
        p.Id == PowerXUltimatePerformance || p.Id == UltimatePerformanceTemplate ||
        p.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase);

    /// <summary>Create the "Ultimate Performance" plan if it is not present, then return its id.</summary>
    public static (ActionResult Result, Guid? Id) EnsureUltimatePerformance()
    {
        var existing = List().FirstOrDefault(IsUltimate);
        if (existing is not null) return (ActionResult.Ok, existing.Id);

        var (ok, output) = Run($"-duplicatescheme {UltimatePerformanceTemplate:D} {PowerXUltimatePerformance:D}");
        if (!ok) return (ActionResult.Fail(output.Trim()), null);
        return (ActionResult.Ok, PowerXUltimatePerformance);
    }

    private static (bool ok, string output) Run(string args)
    {
        var r = ProcessRunner.Run("powercfg.exe", args, 10_000);
        return (r.Ok, r.Output);
    }
}
