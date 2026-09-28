using Microsoft.Win32;

namespace PowerX.Core.Tweaks;

public enum RegistryHive2 { CurrentUser, LocalMachine, ClassesRoot, Users }

/// <summary>
/// One registry value this tweak owns. <see cref="AppliedValue"/> is set when the tweak is on;
/// <see cref="DefaultValue"/> is what Windows ships (null = the value does not exist by default,
/// so revert deletes it). Set <see cref="DeleteKeyTreeOnRevert"/> when the *presence of the key*
/// (not a value) is what activates the tweak — e.g. the Win11 classic context-menu shim.
/// </summary>
public sealed record RegistryValueSpec(
    RegistryHive2 Hive,
    string SubKey,
    string Name,
    RegistryValueKind Kind,
    object AppliedValue,
    object? DefaultValue)
{
    public bool DeleteKeyTreeOnRevert { get; init; }
}

/// <summary>
/// Generic reversible registry tweak. Detection compares the live value against
/// <see cref="RegistryValueSpec.AppliedValue"/> / <see cref="RegistryValueSpec.DefaultValue"/>.
/// A tweak with multiple values is "Applied" only when every value matches its applied state.
/// </summary>
public sealed class RegistryTweakOperation(params RegistryValueSpec[] values) : ITweakOperation
{
    private readonly RegistryValueSpec[] _values = values.Length > 0
        ? values
        : throw new ArgumentException("At least one value spec required", nameof(values));

    public TweakState Detect(TweakContext context)
    {
        int applied = 0, def = 0;
        foreach (var v in _values)
        {
            object? live = ReadValue(v);

            // An absent value means "Windows' shipped behaviour" — i.e. the default — unless
            // this tweak's *applied* state is itself "value absent" (AppliedValue == null).
            if (live is null)
            {
                if (v.AppliedValue is null) applied++;
                else def++;
                continue;
            }

            if (ValueEquals(live, v.AppliedValue)) applied++;
            else if (ValueEquals(live, v.DefaultValue)) def++;
        }
        if (applied == _values.Length) return TweakState.Applied;
        if (def == _values.Length) return TweakState.Default;
        return TweakState.Custom;
    }

    public TweakOutcome Apply(TweakContext context) => Write(context, revert: false);

    public TweakOutcome Revert(TweakContext context) => Write(context, revert: true);

    public bool Verify(TweakContext context)
    {
        var state = Detect(context);
        return state is TweakState.Applied or TweakState.Default;
    }

    private TweakOutcome Write(TweakContext context, bool revert)
    {
        var target = revert ? TweakState.Default : TweakState.Applied;
        var snapshots = new List<WriteSnapshot>(_values.Length);

        try
        {
            if (Detect(context) == target) return TweakOutcome.NoChange(target);
            if (context.DryRun) return TweakOutcome.Ok(target, "dry-run");

            foreach (var v in _values)
            {
                var snapshot = Capture(v);
                var expected = ExpectedAfterWrite(v, revert);
                snapshots.Add(new WriteSnapshot(snapshot, expected, Attempted: true));
                WriteValue(v, revert);
            }

            var result = Detect(context);
            if (result != target)
                throw new InvalidOperationException($"Post-write state was {result}, expected {target}.");
            return TweakOutcome.Ok(result);
        }
        catch (Exception ex)
        {
            var compensationErrors = Compensate(snapshots);
            if (snapshots.Any(s => s.Expected.KeyAbsent))
                compensationErrors.Add("A deleted subkey may have contained other values that were not captured.");
            TweakState observed;
            try { observed = Detect(context); }
            catch { observed = TweakState.Unknown; }

            string reason = ex is UnauthorizedAccessException
                ? "Windows denied permission (elevation required)."
                : ex.Message;
            if (snapshots.Count == 0)
                reason += " No writes were made.";
            else if (compensationErrors.Count == 0)
                reason += " Earlier writes were rolled back where the live values still matched this attempt.";
            else
                reason += " Compensation was incomplete: " + string.Join("; ", compensationErrors);
            reason += $" Live state after the attempt: {observed}.";
            return new TweakOutcome(false, observed, reason);
        }
    }

    private static void WriteValue(RegistryValueSpec v, bool revert)
    {
        using var root = OpenRoot(v.Hive);
        if (revert && v.DeleteKeyTreeOnRevert)
        {
            root.DeleteSubKeyTree(v.SubKey, throwOnMissingSubKey: false);
            return;
        }

        object? value = revert ? v.DefaultValue : v.AppliedValue;
        if (value is null)
        {
            using var key = root.OpenSubKey(v.SubKey, writable: true);
            key?.DeleteValue(v.Name, throwOnMissingValue: false);
            return;
        }

        using var writable = root.CreateSubKey(v.SubKey, writable: true);
        writable.SetValue(v.Name, value, v.Kind);
    }

    private static CapturedValue Capture(RegistryValueSpec v)
    {
        using var root = OpenRoot(v.Hive);
        using var key = root.OpenSubKey(v.SubKey);
        if (key is null) return new CapturedValue(v, KeyExisted: false, ValueExisted: false, null, null);

        bool exists = key.GetValueNames().Any(name => name.Equals(v.Name, StringComparison.OrdinalIgnoreCase));
        if (!exists) return new CapturedValue(v, KeyExisted: true, ValueExisted: false, null, null);

        var kind = key.GetValueKind(v.Name);
        var value = key.GetValue(v.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        return new CapturedValue(v, KeyExisted: true, ValueExisted: true, value, kind);
    }

    private static ExpectedValue ExpectedAfterWrite(RegistryValueSpec v, bool revert)
    {
        bool keyAbsent = revert && v.DeleteKeyTreeOnRevert;
        object? value = revert ? v.DefaultValue : v.AppliedValue;
        return new ExpectedValue(
            KeyAbsent: keyAbsent,
            ValueExists: !keyAbsent && value is not null,
            Value: value,
            Kind: !keyAbsent && value is not null ? v.Kind : null);
    }

    private static List<string> Compensate(List<WriteSnapshot> snapshots)
    {
        var errors = new List<string>();
        foreach (var snapshot in snapshots.AsEnumerable().Reverse())
        {
            try
            {
                var original = snapshot.Original;
                var current = Capture(original.Spec);
                if (MatchesOriginal(original, current)) continue;

                if (!MatchesExpected(snapshot.Expected, current))
                {
                    // A failed write may have created an empty key before failing at SetValue.
                    // Remove only that newly-created empty key; never remove concurrent content.
                    if (!original.KeyExisted && current.KeyExisted && !current.ValueExisted && snapshot.Attempted)
                    {
                        using var root = OpenRoot(original.Spec.Hive);
                        root.DeleteSubKey(original.Spec.SubKey, throwOnMissingSubKey: false);
                        continue;
                    }

                    errors.Add($"{original.Spec.Hive}\\{original.Spec.SubKey}\\{original.Spec.Name} changed after the write and was left untouched");
                    continue;
                }

                Restore(original);
            }
            catch (Exception ex)
            {
                errors.Add($"{snapshot.Original.Spec.Hive}\\{snapshot.Original.Spec.SubKey}\\{snapshot.Original.Spec.Name}: {ex.Message}");
            }
        }
        return errors;
    }

    private static bool MatchesOriginal(CapturedValue original, CapturedValue current) =>
        original.KeyExisted == current.KeyExisted &&
        original.ValueExisted == current.ValueExisted &&
        (!original.ValueExisted ||
         (original.Kind == current.Kind && ExactValueEquals(original.Value, current.Value)));

    private static bool MatchesExpected(ExpectedValue expected, CapturedValue current)
    {
        if (expected.KeyAbsent) return !current.KeyExisted;
        if (!expected.ValueExists)
            return current.KeyExisted && !current.ValueExisted;
        return current.ValueExisted && current.Kind == expected.Kind && ExactValueEquals(current.Value, expected.Value);
    }

    private static void Restore(CapturedValue original)
    {
        using var root = OpenRoot(original.Spec.Hive);
        if (!original.KeyExisted)
        {
            using (var key = root.OpenSubKey(original.Spec.SubKey, writable: true))
                key?.DeleteValue(original.Spec.Name, throwOnMissingValue: false);
            root.DeleteSubKey(original.Spec.SubKey, throwOnMissingSubKey: false);
            return;
        }

        using var writable = root.CreateSubKey(original.Spec.SubKey, writable: true);
        if (original.ValueExisted)
            writable.SetValue(original.Spec.Name, original.Value!, original.Kind!.Value);
        else
            writable.DeleteValue(original.Spec.Name, throwOnMissingValue: false);
    }

    private static bool ExactValueEquals(object? left, object? right) => (left, right) switch
    {
        (byte[] a, byte[] b) => a.AsSpan().SequenceEqual(b),
        (string[] a, string[] b) => a.SequenceEqual(b, StringComparer.Ordinal),
        _ => Equals(left, right),
    };

    private sealed record CapturedValue(
        RegistryValueSpec Spec,
        bool KeyExisted,
        bool ValueExisted,
        object? Value,
        RegistryValueKind? Kind);

    private sealed record WriteSnapshot(CapturedValue Original, ExpectedValue Expected, bool Attempted);
    private sealed record ExpectedValue(bool KeyAbsent, bool ValueExists, object? Value, RegistryValueKind? Kind);

    private static object? ReadValue(RegistryValueSpec v)
    {
        try
        {
            using var root = OpenRoot(v.Hive);
            using var key = root.OpenSubKey(v.SubKey);
            return key?.GetValue(v.Name);
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException)
        {
            // The key exists but we cannot read it — almost always missing elevation. Do NOT
            // report this as "Default"; let it surface as Unknown so the UI can say why.
            throw new InvalidOperationException(
                $"Could not read {v.Hive}\\{v.SubKey}\\{v.Name}: {ex.Message} Elevation may be required.", ex);
        }
        catch (Exception)
        {
            // Any other unexpected failure: treat the value as absent rather than crash detection.
            return null;
        }
    }

    private static RegistryKey OpenRoot(RegistryHive2 hive) => hive switch
    {
        RegistryHive2.CurrentUser => Registry.CurrentUser,
        RegistryHive2.LocalMachine => Registry.LocalMachine,
        RegistryHive2.ClassesRoot => Registry.ClassesRoot,
        RegistryHive2.Users => Registry.Users,
        _ => throw new ArgumentOutOfRangeException(nameof(hive)),
    };

    private static bool ValueEquals(object? live, object? expected)
    {
        if (live is null || expected is null) return live is null && expected is null;
        if (live is int li && expected is int ei) return li == ei;
        return string.Equals(Convert.ToString(live), Convert.ToString(expected), StringComparison.OrdinalIgnoreCase);
    }
}
