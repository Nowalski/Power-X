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

    [GeneratedRegex(@"Power Scheme GUID:\s*([0-9a-fA-F-]{36})\s*\(([^)]*)\)(\s*\*)?")]
    private static partial Regex PlanLine();

    public static IReadOnlyList<PowerPlan> List()
    {
        var (ok, output) = Run("/list");
        if (!ok) return [];
        var plans = new List<PowerPlan>();
        foreach (Match m in PlanLine().Matches(output))
        {
            if (Guid.TryParse(m.Groups[1].Value, out var id))
                plans.Add(new PowerPlan(id, m.Groups[2].Value.Trim(), m.Groups[3].Success));
        }
        return plans;
    }

    public static ActionResult Activate(Guid planId)
    {
        var (ok, output) = Run($"/setactive {planId:D}");
        return ok ? ActionResult.Ok : ActionResult.Fail(output.Trim());
    }

    /// <summary>Create the "Ultimate Performance" plan if it is not present, then return its id.</summary>
    public static (ActionResult Result, Guid? Id) EnsureUltimatePerformance()
    {
        var existing = List().FirstOrDefault(p => p.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase));
        if (existing is not null) return (ActionResult.Ok, existing.Id);

        var (ok, output) = Run($"-duplicatescheme {UltimatePerformanceTemplate:D}");
        if (!ok) return (ActionResult.Fail(output.Trim()), null);

        var m = Regex.Match(output, @"([0-9a-fA-F-]{36})");
        return m.Success && Guid.TryParse(m.Value, out var id)
            ? (ActionResult.Ok, id)
            : (ActionResult.Ok, List().FirstOrDefault(p => p.Name.Contains("Ultimate", StringComparison.OrdinalIgnoreCase))?.Id);
    }

    private static (bool ok, string output) Run(string args)
    {
        var r = ProcessRunner.Run("powercfg.exe", args, 10_000);
        return (r.Ok, r.Output);
    }
}
