using Microsoft.Win32;

namespace PowerX.Core.Tweaks;

/// <summary>
/// Pins Windows to its currently-installed feature-update version via the documented
/// "Select the target Feature Update version" Group Policy keys. This is the reversible
/// mechanism dedicated "pause updates" tools use — security/quality updates keep flowing,
/// feature updates are held until the pin is removed.
/// </summary>
public sealed class TargetReleaseOperation : ITweakOperation
{
    private const string PolicyKey = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";
    private static readonly string[] ValueNames = ["TargetReleaseVersion", "TargetReleaseVersionInfo", "ProductVersion"];

    public TweakState Detect(TweakContext context)
    {
        using var key = Registry.LocalMachine.OpenSubKey(PolicyKey);
        var trv = key?.GetValue("TargetReleaseVersion");
        var info = key?.GetValue("TargetReleaseVersionInfo") as string;
        var product = key?.GetValue("ProductVersion") as string;
        if (trv is 1 && !string.IsNullOrWhiteSpace(info) &&
            string.Equals(product, ProductName(), StringComparison.OrdinalIgnoreCase)) return TweakState.Applied;
        if (trv is null && info is null && product is null) return TweakState.Default;
        return TweakState.Custom;
    }

    public TweakOutcome Apply(TweakContext context)
    {
        if (Detect(context) == TweakState.Applied) return TweakOutcome.NoChange(TweakState.Applied);
        if (context.DryRun) return TweakOutcome.Ok(TweakState.Applied, "dry-run");

        string display = CurrentDisplayVersion();
        if (string.IsNullOrWhiteSpace(display))
            return TweakOutcome.Fail("Could not read the current Windows feature-update version.");

        return WriteBatch(
        [
            ("TargetReleaseVersion", (object)1, RegistryValueKind.DWord),
            ("TargetReleaseVersionInfo", display, RegistryValueKind.String),
            ("ProductVersion", ProductName(), RegistryValueKind.String),
        ], revert: false, successMessage: $"Pinned to {display}");
    }

    public TweakOutcome Revert(TweakContext context)
    {
        if (Detect(context) == TweakState.Default) return TweakOutcome.NoChange(TweakState.Default);
        if (context.DryRun) return TweakOutcome.Ok(TweakState.Default, "dry-run");
        return WriteBatch([], revert: true, successMessage: null);
    }

    private TweakOutcome WriteBatch(
        IReadOnlyList<(string Name, object Value, RegistryValueKind Kind)> writes,
        bool revert,
        string? successMessage)
    {
        bool keyExisted;
        using (var existing = Registry.LocalMachine.OpenSubKey(PolicyKey))
            keyExisted = existing is not null;
        var original = ValueNames.Select(CaptureValue).ToArray();
        var expected = writes.ToDictionary(w => w.Name,
            w => new ValueSnapshot(w.Value, true, w.Kind), StringComparer.OrdinalIgnoreCase);

        try
        {
            using var key = revert
                ? Registry.LocalMachine.OpenSubKey(PolicyKey, writable: true)
                : Registry.LocalMachine.CreateSubKey(PolicyKey, writable: true);
            if (key is not null)
            {
                foreach (string name in ValueNames)
                {
                    if (revert) key.DeleteValue(name, throwOnMissingValue: false);
                    else if (expected.TryGetValue(name, out var value)) key.SetValue(name, value.Value!, value.Kind!.Value);
                }
            }

            TweakState observed = Detect(TweakContext.Detect());
            TweakState target = revert ? TweakState.Default : TweakState.Applied;
            if (observed != target)
                throw new InvalidOperationException($"Post-write state was {observed}, expected {target}.");
            return TweakOutcome.Ok(target, successMessage);
        }
        catch (Exception ex)
        {
            var errors = RestoreValues(original, expected);
            if (!keyExisted)
            {
                try
                {
                    using var root = Registry.LocalMachine;
                    using var key = root.OpenSubKey(PolicyKey);
                    if (key is not null && key.ValueCount == 0 && key.SubKeyCount == 0)
                        root.DeleteSubKey(PolicyKey, throwOnMissingSubKey: false);
                }
                catch (Exception cleanupError) { errors.Add(cleanupError.Message); }
            }

            TweakState observed;
            try { observed = Detect(TweakContext.Detect()); }
            catch { observed = TweakState.Unknown; }

            string message = ex is UnauthorizedAccessException
                ? "Administrator rights required."
                : ex.Message;
            message += errors.Count == 0
                ? " Earlier writes were rolled back where the live values still matched this attempt."
                : " Compensation was incomplete: " + string.Join("; ", errors);
            message += $" Live state after the attempt: {observed}.";
            return new TweakOutcome(false, observed, message);
        }
    }

    private static ValueSnapshot CaptureValue(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(PolicyKey);
        if (key is null || !key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
            return new ValueSnapshot(null, false, null);

        var kind = key.GetValueKind(name);
        return new ValueSnapshot(key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames), true, kind);
    }

    private static List<string> RestoreValues(ValueSnapshot[] original,
        IReadOnlyDictionary<string, ValueSnapshot> expected)
    {
        var errors = new List<string>();
        foreach (string name in ValueNames.Reverse())
        {
            var before = original[Array.FindIndex(ValueNames, n => n.Equals(name, StringComparison.OrdinalIgnoreCase))];
            try
            {
                var current = CaptureValue(name);
                if (SameValue(before, current)) continue;
                bool matchesAttempt = expected.TryGetValue(name, out var written)
                    ? SameValue(written, current)
                    : !current.Exists;
                if (!matchesAttempt)
                {
                    errors.Add($"{name} changed after the write and was left untouched");
                    continue;
                }

                using var key = Registry.LocalMachine.CreateSubKey(PolicyKey, writable: true);
                if (key is null) throw new InvalidOperationException($"Could not open {PolicyKey} for compensation.");
                if (before.Exists) key.SetValue(name, before.Value!, before.Kind!.Value);
                else key.DeleteValue(name, throwOnMissingValue: false);
            }
            catch (Exception ex) { errors.Add($"{name}: {ex.Message}"); }
        }
        return errors;
    }

    private static bool SameValue(ValueSnapshot left, ValueSnapshot right) =>
        left.Exists == right.Exists && (!left.Exists ||
            (left.Kind == right.Kind && Equals(left.Value, right.Value)));

    private sealed record ValueSnapshot(object? Value, bool Exists, RegistryValueKind? Kind);

    public bool Verify(TweakContext context) => Detect(context) is TweakState.Applied or TweakState.Default;

    private static string CurrentDisplayVersion()
    {
        using var cv = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        return cv?.GetValue("DisplayVersion")?.ToString()
               ?? cv?.GetValue("ReleaseId")?.ToString()
               ?? "";
    }

    private static string ProductName() =>
        Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10";
}
