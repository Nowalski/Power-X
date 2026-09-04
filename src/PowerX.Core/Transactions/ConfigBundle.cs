using System.Runtime.Versioning;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerX.Core.Debloat;
using PowerX.Core.Tweaks;

namespace PowerX.Core.Transactions;

/// <summary>A portable, reviewable description of a PowerX setup: which tweaks are applied and
/// which curated apps were removed. No machine or user detail; safe to share.</summary>
public sealed record ConfigBundle
{
    public const string CurrentSchema = "powerx.config/1";
    public string Schema { get; init; } = "";
    public DateTimeOffset Exported { get; init; } = DateTimeOffset.Now;
    public string PowerXVersion { get; init; } = "";
    public string WindowsBuild { get; init; } = "";

    /// <summary>IDs of tweaks that were in the Applied state on the source machine.</summary>
    public List<string> AppliedTweaks { get; init; } = [];

    /// <summary>PackageFamilyName fragments of curated apps that were removed on the source machine.</summary>
    public List<string> RemovedApps { get; init; } = [];
}

public sealed record BundlePlanItem(string Id, string Label, string Detail, bool Actionable);

/// <summary>What importing a bundle would do on THIS machine, shown before anything is applied.</summary>
public sealed record BundlePlan(
    IReadOnlyList<BundlePlanItem> TweaksToApply,
    IReadOnlyList<BundlePlanItem> TweaksAlreadyApplied,
    IReadOnlyList<BundlePlanItem> AppsToRemove,
    IReadOnlyList<BundlePlanItem> AppsNotPresent,
    IReadOnlyList<string> Warnings)
{
    public bool AnyAction => TweaksToApply.Count > 0 || AppsToRemove.Count > 0;
}

[SupportedOSPlatform("windows")]
public static class ConfigBundleService
{
    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ---------------------------------------------------------------- export

    public static ConfigBundle Export()
    {
        var engine = new TweakEngine(TweakCatalog.Default);
        var applied = engine.GetAllStatus()
            .Where(s => s.State == TweakState.Applied)
            .Select(s => s.Definition.Id)
            .OrderBy(x => x)
            .ToList();

        string build = "";
        try
        {
            using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            build = k?.GetValue("CurrentBuildNumber")?.ToString() ?? "";
        }
        catch { }

        return new ConfigBundle
        {
            Schema = ConfigBundle.CurrentSchema,
            PowerXVersion = typeof(ConfigBundleService).Assembly.GetName().Version?.ToString(3) ?? "",
            WindowsBuild = build,
            AppliedTweaks = applied,
        };
    }

    public static void ExportWithRemovedApps(ConfigBundle bundle, IEnumerable<string> removedFamilyFragments)
        => bundle.RemovedApps.AddRange(removedFamilyFragments.Distinct());

    public static string ToJson(ConfigBundle bundle) => JsonSerializer.Serialize(bundle, Json);

    public static ConfigBundle? FromJson(string json)
    {
        try
        {
            var b = JsonSerializer.Deserialize<ConfigBundle>(json, Json);
            return b?.Schema.StartsWith("powerx.config/", StringComparison.Ordinal) == true ? b : null;
        }
        catch (Exception) { return null; }
    }

    // ---------------------------------------------------------------- plan

    /// <summary>Work out what applying <paramref name="bundle"/> would change on this machine.</summary>
    public static BundlePlan Plan(ConfigBundle bundle, IReadOnlyList<InstalledAppLite> installedApps)
    {
        var engine = new TweakEngine(TweakCatalog.Default);
        var status = engine.GetAllStatus().ToDictionary(s => s.Definition.Id, s => s);

        var toApply = new List<BundlePlanItem>();
        var already = new List<BundlePlanItem>();
        var warnings = new List<string>();

        foreach (var id in bundle.AppliedTweaks)
        {
            if (!status.TryGetValue(id, out var s))
            {
                warnings.Add($"Tweak \"{id}\" is not in this build of PowerX and was skipped.");
                continue;
            }
            var item = new BundlePlanItem(id, s.Definition.Name, s.Definition.WhatItDoes, true);
            if (s.State == TweakState.Applied) already.Add(item with { Actionable = false });
            else if (s.State == TweakState.NotApplicable)
                warnings.Add($"\"{s.Definition.Name}\" does not apply to this Windows build and was skipped.");
            else toApply.Add(item);
        }

        var toRemove = new List<BundlePlanItem>();
        var notPresent = new List<BundlePlanItem>();
        foreach (var frag in bundle.RemovedApps)
        {
            var match = installedApps.FirstOrDefault(a =>
                a.PackageFamilyName.Contains(frag, StringComparison.OrdinalIgnoreCase));
            if (match is null)
                notPresent.Add(new BundlePlanItem(frag, frag, "Not installed on this PC.", false));
            else
                toRemove.Add(new BundlePlanItem(match.PackageFamilyName, match.DisplayName,
                    "Installed here; import can remove it.", true));
        }

        return new BundlePlan(toApply, already, toRemove, notPresent, warnings);
    }

    /// <summary>Apply the tweak half of a plan. App removal is done by the caller through the
    /// existing debloat flow so its own confirmation and undo apply.</summary>
    public static TransactionResult ApplyTweaks(BundlePlan plan)
    {
        var engine = new TweakEngine(TweakCatalog.Default);
        return engine.ApplyMany(plan.TweaksToApply.Select(t => t.Id), ChangeAction.Apply);
    }
}

/// <summary>Minimal installed-app shape the planner needs, so Core's Transactions layer does not
/// take a dependency on the Appx runtime.</summary>
public sealed record InstalledAppLite(string DisplayName, string PackageFamilyName);
