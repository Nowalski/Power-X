namespace PowerX.Core.Tweaks;

public enum ProfileTone { Conservative, Balanced, Aggressive, Restore }

/// <summary>
/// A named, visible set of tweak IDs. A profile is never a hidden script. The UI shows exactly
/// which tweaks it will change before applying, and every one is individually reversible.
/// Security-trade-off and destructive tweaks are never in a built-in profile.
/// </summary>
public sealed record OptimizationProfile(
    string Id,
    string Name,
    string Description,
    ProfileTone Tone,
    IReadOnlyList<string> TweakIds);

public static class Profiles
{
    public static IReadOnlyList<OptimizationProfile> All { get; } =
    [
        new("recommended", "Recommended", "Safe quality-of-life and privacy changes that almost everyone benefits from. Nothing here changes how Windows behaves in a way that would surprise you.",
            ProfileTone.Conservative,
            [
                "explorer.show-file-extensions",
                "explorer.disable-sync-provider-ads",
                "privacy.advertising-id",
                "privacy.tailored-experiences",
                "privacy.disable-suggested-content",
                "start.disable-recommendations",
                "search.disable-web-results",
                "update.no-auto-restart",
                "privacy.reduce-telemetry",
            ]),

        new("privacy", "Privacy", "Cuts down advertising, tracking and suggested content, and keeps everything compatible. Includes all the privacy items from Recommended plus a few more.",
            ProfileTone.Balanced,
            [
                "privacy.advertising-id",
                "privacy.tailored-experiences",
                "privacy.disable-suggested-content",
                "privacy.disable-lockscreen-facts",
                "privacy.reduce-telemetry",
                "search.disable-web-results",
                "start.disable-recommendations",
                "explorer.disable-sync-provider-ads",
            ]),

        new("lowspec", "Potato mode (low-spec)", "For an old or weak PC. Turns off the visual effects, transparency and background work that cost the most on slow hardware, and trims the taskbar. Reversible from Change history, or by applying Restore defaults.",
            ProfileTone.Aggressive,
            [
                "performance.best-appearance-for-speed",
                "performance.disable-transparency",
                "performance.instant-menus",
                "performance.no-startup-delay",
                "multitasking.disable-snap-assist",
                "taskbar.hide-widgets",
                "gaming.disable-game-dvr",
                "start.disable-recommendations",
                "privacy.disable-suggested-content",
                "explorer.disable-sync-provider-ads",
            ]),

        new("gaming", "Gaming", "Evidence-backed changes only. It does not touch Defender, VBS or anything security-related, and it does not promise more FPS. What you get is fewer background interruptions and no constant capture.",
            ProfileTone.Balanced,
            [
                "gaming.disable-game-dvr",
                "performance.best-appearance-for-speed",
                "performance.no-startup-delay",
                "multitasking.disable-snap-assist",
                "taskbar.hide-widgets",
                "start.disable-recommendations",
                "privacy.disable-suggested-content",
            ]),

        new("restore", "Restore Windows defaults", "Puts every tweak PowerX has applied back to how Windows ships. Reads the change history to know what to undo.",
            ProfileTone.Restore, []),
    ];

    public static OptimizationProfile? Get(string id) => All.FirstOrDefault(p => p.Id == id);
}
