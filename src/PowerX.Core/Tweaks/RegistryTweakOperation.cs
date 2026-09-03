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

        try
        {
            if (Detect(context) == target) return TweakOutcome.NoChange(target);
            if (context.DryRun) return TweakOutcome.Ok(target, "dry-run");

            foreach (var v in _values)
            {
                using var root = OpenRoot(v.Hive);
                if (revert)
                {
                    if (v.DeleteKeyTreeOnRevert)
                    {
                        root.DeleteSubKeyTree(v.SubKey, throwOnMissingSubKey: false);
                    }
                    else if (v.DefaultValue is null)
                    {
                        using var key = root.OpenSubKey(v.SubKey, writable: true);
                        key?.DeleteValue(v.Name, throwOnMissingValue: false);
                    }
                    else
                    {
                        using var key = root.CreateSubKey(v.SubKey, writable: true);
                        key.SetValue(v.Name, v.DefaultValue, v.Kind);
                    }
                }
                else
                {
                    using var key = root.CreateSubKey(v.SubKey, writable: true);
                    key.SetValue(v.Name, v.AppliedValue, v.Kind);
                }
            }

            var result = Detect(context);
            return result == target
                ? TweakOutcome.Ok(result)
                : TweakOutcome.Fail($"Post-write state was {result}, expected {target}");
        }
        catch (UnauthorizedAccessException)
        {
            return TweakOutcome.Fail("Windows denied permission (elevation required).");
        }
        catch (Exception ex)
        {
            return TweakOutcome.Fail(ex.Message);
        }
    }

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
