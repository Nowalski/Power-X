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

    public TweakState Detect(TweakContext context)
    {
        using var key = Registry.LocalMachine.OpenSubKey(PolicyKey);
        var trv = key?.GetValue("TargetReleaseVersion");
        var info = key?.GetValue("TargetReleaseVersionInfo") as string;
        if (trv is 1 && !string.IsNullOrWhiteSpace(info)) return TweakState.Applied;
        if (trv is null && info is null) return TweakState.Default;
        return TweakState.Custom;
    }

    public TweakOutcome Apply(TweakContext context)
    {
        if (Detect(context) == TweakState.Applied) return TweakOutcome.NoChange(TweakState.Applied);
        if (context.DryRun) return TweakOutcome.Ok(TweakState.Applied, "dry-run");

        string display = CurrentDisplayVersion();
        if (string.IsNullOrWhiteSpace(display))
            return TweakOutcome.Fail("Could not read the current Windows feature-update version.");

        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(PolicyKey, writable: true);
            key.SetValue("TargetReleaseVersion", 1, RegistryValueKind.DWord);
            key.SetValue("TargetReleaseVersionInfo", display, RegistryValueKind.String);
            key.SetValue("ProductVersion", ProductName(), RegistryValueKind.String);
            return TweakOutcome.Ok(TweakState.Applied, $"Pinned to {display}");
        }
        catch (UnauthorizedAccessException) { return TweakOutcome.Fail("Administrator rights required."); }
        catch (Exception ex) { return TweakOutcome.Fail(ex.Message); }
    }

    public TweakOutcome Revert(TweakContext context)
    {
        if (Detect(context) == TweakState.Default) return TweakOutcome.NoChange(TweakState.Default);
        if (context.DryRun) return TweakOutcome.Ok(TweakState.Default, "dry-run");
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(PolicyKey, writable: true);
            if (key is not null)
            {
                key.DeleteValue("TargetReleaseVersion", throwOnMissingValue: false);
                key.DeleteValue("TargetReleaseVersionInfo", throwOnMissingValue: false);
                key.DeleteValue("ProductVersion", throwOnMissingValue: false);
            }
            return TweakOutcome.Ok(TweakState.Default);
        }
        catch (UnauthorizedAccessException) { return TweakOutcome.Fail("Administrator rights required."); }
        catch (Exception ex) { return TweakOutcome.Fail(ex.Message); }
    }

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
