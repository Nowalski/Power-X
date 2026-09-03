using Microsoft.Win32;

namespace PowerX.Core.Tweaks;

/// <summary>
/// The verified tweak set. Every entry is HKCU-scoped (no elevation), fully reversible, and
/// carries at least one citation. Additions must follow docs/CONTRIBUTING.md ("Adding a tweak")
/// and be listed in docs/TWEAK_CATALOG.md. No undocumented registry constants.
/// </summary>
public static class TweakCatalog
{
    private const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    public static IReadOnlyList<TweakDefinition> Default { get; } =
    [
        new TweakDefinition
        {
            Id = "explorer.show-file-extensions",
            Name = "Show file name extensions",
            Category = "File Explorer",
            Tags = ["explorer", "extensions", "safety"],
            WhatItDoes = "Shows the file type (.exe, .pdf, .txt) on every file in Explorer.",
            WhyYouMightWant = "With extensions hidden it is easy to mistake invoice.pdf.exe for a PDF. Showing them is a basic safety win.",
            Downside = "File names look a little busier. Nothing stops working.",
            Risk = TweakRisk.Low,
            Restart = RestartScope.Explorer,
            Recommended = true,
            Sources = [new Evidence("Windows shell HideFileExt value", "https://learn.microsoft.com/windows/win32/shell/how-to-customize-the-file-icon-overlay")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, Advanced, "HideFileExt", RegistryValueKind.DWord, 0, 1)),
        },

        new TweakDefinition
        {
            Id = "explorer.show-hidden-files",
            Name = "Show hidden files and folders",
            Category = "File Explorer",
            Tags = ["explorer", "hidden", "advanced"],
            WhatItDoes = "Shows files and folders that have the Hidden attribute. Protected operating-system files stay hidden.",
            WhyYouMightWant = "Handy when you are troubleshooting or clearing out per-user app data.",
            Downside = "More clutter in day-to-day browsing, and it is easier to move or delete something by accident.",
            Risk = TweakRisk.Moderate,
            Restart = RestartScope.Explorer,
            Recommended = false,
            Sources = [new Evidence("Explorer Advanced\\Hidden: 1 = show, 2 = don't show")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, Advanced, "Hidden", RegistryValueKind.DWord, 1, 2)),
        },

        new TweakDefinition
        {
            Id = "explorer.launch-to-this-pc",
            Name = "Open File Explorer to This PC",
            Category = "File Explorer",
            Tags = ["explorer", "navigation"],
            WhatItDoes = "Makes Explorer open on \"This PC\" instead of \"Home\".",
            WhyYouMightWant = "Quicker access to your drives, and it skips the Home page's recent and recommended files.",
            Downside = "You lose the quick recent-files view on the Home page.",
            Risk = TweakRisk.Low,
            Restart = RestartScope.Explorer,
            Recommended = false,
            Sources = [new Evidence("Explorer Advanced\\LaunchTo: 1 = This PC, 2 = Home, 3 = Downloads")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, Advanced, "LaunchTo", RegistryValueKind.DWord, 1, 2)),
        },

        new TweakDefinition
        {
            Id = "privacy.advertising-id",
            Name = "Disable the advertising ID",
            Category = "Privacy",
            Tags = ["privacy", "advertising", "tracking"],
            WhatItDoes = "Turns off the per-user advertising identifier that apps use to build an ad profile of you.",
            WhyYouMightWant = "Cuts down cross-app ad tracking. Same as the \"Let apps show me personalised ads\" switch in Settings, under Privacy & security > General.",
            Downside = "The ads you see get less targeted. Nothing else changes.",
            Risk = TweakRisk.Low,
            Recommended = true,
            Sources = [new Evidence("AdvertisingInfo\\Enabled", "https://learn.microsoft.com/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services#7-advertising-id")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser,
                    @"Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo", "Enabled",
                    RegistryValueKind.DWord, 0, null)),
        },

        new TweakDefinition
        {
            Id = "start.disable-recommendations",
            Name = "Reduce Start menu recommendations",
            Category = "Start",
            Tags = ["start", "privacy", "suggestions"],
            WhatItDoes = "Stops the Start menu showing tips, app promotions and recommended website shortcuts.",
            WhyYouMightWant = "A quieter Start menu with less advertising in it.",
            Downside = "The recommended area no longer shows suggested content. Your recent files still appear there.",
            Risk = TweakRisk.Low,
            MinBuild = 22621,
            Recommended = true,
            Sources = [new Evidence("Start_IrisRecommendations under Explorer\\Advanced")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, Advanced, "Start_IrisRecommendations", RegistryValueKind.DWord, 0, 1)),
        },

        new TweakDefinition
        {
            Id = "taskbar.hide-widgets",
            Name = "Hide the Widgets button",
            Category = "Taskbar",
            Tags = ["taskbar", "widgets"],
            WhatItDoes = "Removes the Widgets (weather and news) button from the taskbar.",
            WhyYouMightWant = "Frees up taskbar space and stops the panel opening when you brush past it. A little less background activity if you never use it.",
            Downside = "The Widgets board is no longer one click away. Win+W still opens it unless you also remove the package.",
            Risk = TweakRisk.Low,
            Restart = RestartScope.Explorer,
            MinBuild = 22000,
            Recommended = false,
            Sources = [new Evidence("Explorer Advanced\\TaskbarDa: 0 = hidden, 1 = shown")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, Advanced, "TaskbarDa", RegistryValueKind.DWord, 0, 1)),
        },

        new TweakDefinition
        {
            Id = "gaming.disable-game-dvr",
            Name = "Disable background game recording (Game DVR)",
            Category = "Gaming",
            Tags = ["gaming", "gamebar", "capture"],
            WhatItDoes = "Turns off the background capture path that Xbox Game Bar uses to keep recording your gameplay.",
            WhyYouMightWant = "That background recording costs a bit of performance in some games. If you never grab clips, this stops the work.",
            Downside = "\"Record the last 30 seconds\" and background clips stop working. Manual capture in Game Bar may also stop.",
            Risk = TweakRisk.Moderate,
            Restart = RestartScope.SignOut,
            Recommended = false,
            Sources =
            [
                new Evidence("GameConfigStore\\GameDVR_Enabled and CurrentVersion\\GameDVR\\AppCaptureEnabled"),
                new Evidence("Independent benchmarks put the effect at roughly 1 to 3 percent average FPS, and it varies by game. Not the big gains often claimed."),
            ],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, @"System\GameConfigStore", "GameDVR_Enabled", RegistryValueKind.DWord, 0, 1),
                new RegistryValueSpec(RegistryHive2.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\GameDVR", "AppCaptureEnabled", RegistryValueKind.DWord, 0, 1)),
        },

        // ---------------------------------------------------------------- Privacy

        new TweakDefinition
        {
            Id = "privacy.tailored-experiences",
            Name = "Turn off tailored experiences",
            Category = "Privacy",
            Tags = ["privacy", "telemetry", "suggestions"],
            WhatItDoes = "Stops Windows using your diagnostic data to personalise tips, ads and recommendations.",
            WhyYouMightWant = "Less profiling of how you use your PC. Matches the \"Tailored experiences\" switch in Settings, under Privacy & security > Diagnostics & feedback.",
            Downside = "Suggestions in Settings and on the lock screen become generic.",
            Risk = TweakRisk.Low,
            Recommended = true,
            Sources = [new Evidence("Privacy\\TailoredExperiencesWithDiagnosticDataEnabled", "https://learn.microsoft.com/windows/privacy/manage-connections-from-windows-operating-system-components-to-microsoft-services")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", RegistryValueKind.DWord, 0, 1)),
        },

        new TweakDefinition
        {
            Id = "privacy.disable-suggested-content",
            Name = "Stop suggested content in Settings and Start",
            Category = "Privacy",
            Tags = ["privacy", "ads", "suggestions", "start"],
            WhatItDoes = "Turns off the Content Delivery Manager feeds that push app promotions, suggested content and tips into Settings, Start and notifications.",
            WhyYouMightWant = "A cleaner OS with less advertising baked in. None of this content does anything useful.",
            Downside = "You no longer see Microsoft's suggested apps or feature tips.",
            Risk = TweakRisk.Low,
            Recommended = true,
            Sources = [new Evidence("ContentDeliveryManager SubscribedContent-* and SystemPaneSuggestionsEnabled, the values Settings flips")],
            Operation = new RegistryTweakOperation(
                Cdm("SystemPaneSuggestionsEnabled"),
                Cdm("SubscribedContent-338393Enabled"),
                Cdm("SubscribedContent-353694Enabled"),
                Cdm("SubscribedContent-353696Enabled"),
                Cdm("SubscribedContent-338388Enabled")),
        },

        new TweakDefinition
        {
            Id = "privacy.disable-lockscreen-facts",
            Name = "Hide lock screen tips and fun facts",
            Category = "Privacy",
            Tags = ["privacy", "lockscreen", "spotlight"],
            WhatItDoes = "Stops Windows Spotlight overlaying tips, ads and trivia on the lock screen.",
            WhyYouMightWant = "A clean lock screen image with no text or promotions on top of it.",
            Downside = "You lose the like and dislike buttons for the Spotlight picture, and the daily fact.",
            Risk = TweakRisk.Low,
            Recommended = false,
            Sources = [new Evidence("ContentDeliveryManager RotatingLockScreenOverlayEnabled / SubscribedContent-338387Enabled")],
            Operation = new RegistryTweakOperation(
                Cdm("RotatingLockScreenOverlayEnabled"),
                Cdm("SubscribedContent-338387Enabled")),
        },

        new TweakDefinition
        {
            Id = "privacy.disable-pointer-suggestions",
            Name = "Disable \"Enhance pointer precision\" (mouse acceleration)",
            Category = "Input",
            Tags = ["mouse", "input", "gaming"],
            WhatItDoes = "Turns off the Windows mouse acceleration curve, so pointer movement tracks your hand 1:1.",
            WhyYouMightWant = "Consistent aim in games and a cursor that always moves the same distance for the same flick. Matches the \"Enhance pointer precision\" checkbox in Settings > Mouse.",
            Downside = "If you are used to acceleration, slow precise movements feel different at first.",
            Risk = TweakRisk.Low,
            Restart = RestartScope.SignOut,
            Recommended = false,
            Sources = [new Evidence("Control Panel\\Mouse MouseSpeed / MouseThreshold1 / MouseThreshold2, the \"Enhance pointer precision\" checkbox")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, @"Control Panel\Mouse", "MouseSpeed", RegistryValueKind.String, "0", "1"),
                new RegistryValueSpec(RegistryHive2.CurrentUser, @"Control Panel\Mouse", "MouseThreshold1", RegistryValueKind.String, "0", "6"),
                new RegistryValueSpec(RegistryHive2.CurrentUser, @"Control Panel\Mouse", "MouseThreshold2", RegistryValueKind.String, "0", "10")),
        },

        // ---------------------------------------------------------------- File Explorer

        new TweakDefinition
        {
            Id = "explorer.disable-sync-provider-ads",
            Name = "Hide OneDrive and sync provider ads in Explorer",
            Category = "File Explorer",
            Tags = ["explorer", "onedrive", "ads"],
            WhatItDoes = "Turns off the \"sync provider notifications\" that show OneDrive promotions and prompts inside the Explorer window.",
            WhyYouMightWant = "Takes the advertising out of the file manager. OneDrive sync itself is not affected.",
            Downside = "You will not see OneDrive storage prompts in Explorer.",
            Risk = TweakRisk.Low,
            Restart = RestartScope.Explorer,
            Recommended = true,
            Sources = [new Evidence("Explorer Advanced\\ShowSyncProviderNotifications")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, Advanced, "ShowSyncProviderNotifications", RegistryValueKind.DWord, 0, 1)),
        },

        new TweakDefinition
        {
            Id = "explorer.classic-context-menu",
            Name = "Restore the classic right-click menu",
            Category = "File Explorer",
            Tags = ["explorer", "context-menu"],
            WhatItDoes = "Brings back the full Windows 10 style right-click menu, so there is no \"Show more options\" step.",
            WhyYouMightWant = "Every shell extension shows up straight away, with no extra click to reach common items.",
            Downside = "You lose the compact Windows 11 menu and its icon row. A few apps only design for the new one.",
            Risk = TweakRisk.Moderate,
            Restart = RestartScope.Explorer,
            MinBuild = 22000,
            Recommended = false,
            Sources = [new Evidence("Empty InprocServer32 under CLSID {86ca1aa0-34aa-4e8b-a509-50c905bae2a2}, the shim used widely and reversibly")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser,
                    @"Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}\InprocServer32", "",
                    RegistryValueKind.String, "", null) { DeleteKeyTreeOnRevert = true }),
        },

        new TweakDefinition
        {
            Id = "explorer.compact-mode",
            Name = "Use compact spacing in File Explorer",
            Category = "File Explorer",
            Tags = ["explorer", "density"],
            WhatItDoes = "Tightens the row spacing in Explorer lists (the \"Use compact mode\" folder option).",
            WhyYouMightWant = "More items on screen at once, closer to the Windows 10 density.",
            Downside = "Smaller targets for touch.",
            Risk = TweakRisk.Low,
            Restart = RestartScope.Explorer,
            MinBuild = 22000,
            Recommended = false,
            Sources = [new Evidence("Explorer Advanced\\UseCompactMode")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, Advanced, "UseCompactMode", RegistryValueKind.DWord, 1, 0)),
        },

        // ---------------------------------------------------------------- Taskbar (Windows 11)

        new TweakDefinition
        {
            Id = "taskbar.align-left",
            Name = "Align the taskbar to the left",
            Category = "Taskbar",
            Tags = ["taskbar", "layout"],
            WhatItDoes = "Moves the taskbar icons and the Start button to the left, like Windows 10.",
            WhyYouMightWant = "Start stays in the same corner every time, which suits muscle memory from older Windows.",
            Downside = "None.",
            Risk = TweakRisk.Low,
            Restart = RestartScope.Explorer,
            MinBuild = 22000,
            Recommended = false,
            Sources = [new Evidence("Explorer Advanced\\TaskbarAl: 0 = left, 1 = centre")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, Advanced, "TaskbarAl", RegistryValueKind.DWord, 0, 1)),
        },

        new TweakDefinition
        {
            Id = "taskbar.hide-task-view",
            Name = "Hide the Task View button",
            Category = "Taskbar",
            Tags = ["taskbar", "taskview"],
            WhatItDoes = "Removes the Task View button from the taskbar. Win+Tab still works.",
            WhyYouMightWant = "Taskbar space, if you use the keyboard shortcut or do not use virtual desktops.",
            Downside = "No one-click access to Task View or virtual desktops.",
            Risk = TweakRisk.Low,
            Restart = RestartScope.Explorer,
            Recommended = false,
            Sources = [new Evidence("Explorer Advanced\\ShowTaskViewButton")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, Advanced, "ShowTaskViewButton", RegistryValueKind.DWord, 0, 1)),
        },

        new TweakDefinition
        {
            Id = "taskbar.hide-chat",
            Name = "Hide the Chat / Teams button",
            Category = "Taskbar",
            Tags = ["taskbar", "teams", "chat"],
            WhatItDoes = "Removes the Microsoft Teams (Chat) button from the taskbar.",
            WhyYouMightWant = "You use a different chat app, or none, and want the space back.",
            Downside = "No one-click consumer Teams chat.",
            Risk = TweakRisk.Low,
            Restart = RestartScope.Explorer,
            MinBuild = 22000,
            Recommended = false,
            Sources = [new Evidence("Explorer Advanced\\TaskbarMn")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, Advanced, "TaskbarMn", RegistryValueKind.DWord, 0, 1)),
        },

        new TweakDefinition
        {
            Id = "taskbar.collapse-search",
            Name = "Shrink taskbar search to an icon",
            Category = "Taskbar",
            Tags = ["taskbar", "search"],
            WhatItDoes = "Swaps the wide taskbar search box for a small icon. (You can also hide search entirely; this sets it to the icon.)",
            WhyYouMightWant = "Gets back a big chunk of taskbar width.",
            Downside = "One extra click before you can start typing a search.",
            Risk = TweakRisk.Low,
            Restart = RestartScope.Explorer,
            Recommended = false,
            Sources = [new Evidence(@"Search\SearchboxTaskbarMode: 0 hidden, 1 icon, 2 box")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "SearchboxTaskbarMode", RegistryValueKind.DWord, 1, 2)),
        },

        // ---------------------------------------------------------------- Search

        new TweakDefinition
        {
            Id = "search.disable-web-results",
            Name = "Remove web results from Start search",
            Category = "Search",
            Tags = ["search", "bing", "privacy"],
            WhatItDoes = "Stops the Start menu search box sending your queries to Bing and showing web results.",
            WhyYouMightWant = "Faster search that stays local, and your keystrokes in Start no longer go to a search engine.",
            Downside = "No inline web answers from the Start search box. Open a browser instead.",
            Risk = TweakRisk.Low,
            Restart = RestartScope.Explorer,
            Recommended = true,
            Sources = [new Evidence(@"Search\BingSearchEnabled = 0 (per-user)")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Search", "BingSearchEnabled", RegistryValueKind.DWord, 0, null)),
        },

        // ---------------------------------------------------------------- Multitasking

        new TweakDefinition
        {
            Id = "multitasking.disable-snap-assist",
            Name = "Disable Snap Assist suggestions",
            Category = "Multitasking",
            Tags = ["snap", "multitasking"],
            WhatItDoes = "After you snap a window, Windows stops offering a grid of your other windows to fill the rest of the screen.",
            WhyYouMightWant = "Snapping feels quicker with no follow-up prompt.",
            Downside = "You place the second window yourself.",
            Risk = TweakRisk.Low,
            Recommended = false,
            Sources = [new Evidence("Explorer Advanced\\SnapAssist")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, Advanced, "SnapAssist", RegistryValueKind.DWord, 0, 1)),
        },

        // ---------------------------------------------------------------- Desktop

        new TweakDefinition
        {
            Id = "desktop.show-seconds-in-clock",
            Name = "Show seconds in the taskbar clock",
            Category = "Taskbar",
            Tags = ["clock", "taskbar"],
            WhatItDoes = "Adds a seconds field to the taskbar clock.",
            WhyYouMightWant = "You want the time to the second at a glance.",
            Downside = "A tiny bit of extra work redrawing the clock every second. You will not notice it on modern hardware.",
            Risk = TweakRisk.Low,
            Restart = RestartScope.Explorer,
            MinBuild = 22621,
            Recommended = false,
            Sources = [new Evidence("Explorer Advanced\\ShowSecondsInSystemClock")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, Advanced, "ShowSecondsInSystemClock", RegistryValueKind.DWord, 1, 0)),
        },

        // ---------------------------------------------------------------- Windows Update

        new TweakDefinition
        {
            Id = "update.pin-feature-version",
            Name = "Pause feature updates (pin to the current version)",
            Category = "Windows Update",
            Tags = ["update", "feature-update", "defer"],
            WhatItDoes = "Pins Windows to the feature-update version you are on now. New feature updates (say 24H2 to 25H2) are held back. Monthly security and quality updates keep installing.",
            WhyYouMightWant = "Stay patched but move to a big version on your own schedule. Turning this off lets the next feature update through.",
            Downside = "No new Windows features until you remove the pin. Microsoft eventually forces the update when your version nears end of servicing.",
            Risk = TweakRisk.Advanced,
            Privilege = PrivilegeLevel.Administrator,
            Recommended = false,
            Sources = [new Evidence("Group Policy \"Select the target Feature Update version\": TargetReleaseVersion / TargetReleaseVersionInfo", "https://learn.microsoft.com/windows/deployment/update/waas-configure-wufb")],
            Operation = new TargetReleaseOperation(),
        },

        new TweakDefinition
        {
            Id = "update.defer-quality-updates",
            Name = "Delay monthly quality updates by 30 days",
            Category = "Windows Update",
            Tags = ["update", "quality-update", "defer"],
            WhatItDoes = "Holds each monthly cumulative (security) update for 30 days after release before offering it to you.",
            WhyYouMightWant = "Gives a bad patch time to be pulled or fixed before it reaches your machine.",
            Downside = "You run up to 30 days behind on security fixes. Not a good idea on a machine that faces the internet with nothing else protecting it.",
            Risk = TweakRisk.SecurityTradeoff,
            Privilege = PrivilegeLevel.Administrator,
            Recommended = false,
            Sources = [new Evidence("Group Policy \"Select when Quality Updates are received\": DeferQualityUpdates / DeferQualityUpdatesPeriodInDays")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.LocalMachine, WuPolicy, "DeferQualityUpdates", RegistryValueKind.DWord, 1, null),
                new RegistryValueSpec(RegistryHive2.LocalMachine, WuPolicy, "DeferQualityUpdatesPeriodInDays", RegistryValueKind.DWord, 30, null)),
        },

        new TweakDefinition
        {
            Id = "update.no-auto-restart",
            Name = "Never auto-restart while I'm signed in",
            Category = "Windows Update",
            Tags = ["update", "restart"],
            WhatItDoes = "Windows will not reboot on its own to finish an update while someone is logged on. It waits for you to restart.",
            WhyYouMightWant = "No more losing work to a surprise reboot in the middle of the night or mid-session.",
            Downside = "Updates that need a restart sit pending until you reboot, so you have to remember to do it.",
            Risk = TweakRisk.Low,
            Privilege = PrivilegeLevel.Administrator,
            Recommended = true,
            Sources = [new Evidence("Group Policy \"No auto-restart with logged on users\": AU\\NoAutoRebootWithLoggedOnUsers")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.LocalMachine, WuPolicy + @"\AU", "NoAutoRebootWithLoggedOnUsers", RegistryValueKind.DWord, 1, null)),
        },

        new TweakDefinition
        {
            Id = "update.exclude-drivers",
            Name = "Don't get drivers from Windows Update",
            Category = "Windows Update",
            Tags = ["update", "drivers"],
            WhatItDoes = "Stops Windows Update handing you driver updates alongside quality updates. You still install drivers from the vendor or Device Manager.",
            WhyYouMightWant = "Keeps a Windows-pushed driver from overwriting a working vendor driver. This bites people most often with GPUs and audio.",
            Downside = "Keeping drivers current is now on you.",
            Risk = TweakRisk.Moderate,
            Privilege = PrivilegeLevel.Administrator,
            Recommended = false,
            Sources = [new Evidence("Group Policy \"Do not include drivers with Windows Updates\": ExcludeWUDriversInQualityUpdate")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.LocalMachine, WuPolicy, "ExcludeWUDriversInQualityUpdate", RegistryValueKind.DWord, 1, null)),
        },

        // ---------------------------------------------------------------- Privacy (machine-wide)

        new TweakDefinition
        {
            Id = "privacy.reduce-telemetry",
            Name = "Set diagnostic data to the minimum for this edition",
            Category = "Privacy",
            Tags = ["telemetry", "privacy", "diagtrack"],
            WhatItDoes = "Drops the system diagnostic-data level to the lowest Windows allows on this edition. That is Security on Enterprise and Education, and \"Required only\" everywhere else.",
            WhyYouMightWant = "Sends less data to Microsoft. Matches \"Required diagnostic data\" in Settings, under Privacy > Diagnostics & feedback.",
            Downside = "Home and Pro cannot go below Required. Some Insider and feedback features expect more.",
            Risk = TweakRisk.Low,
            Privilege = PrivilegeLevel.Administrator,
            Recommended = true,
            Sources = [new Evidence("Policy DataCollection\\AllowTelemetry (0 Security, 1 Required)", "https://learn.microsoft.com/windows/privacy/configure-windows-diagnostic-data-in-your-organization")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\DataCollection", "AllowTelemetry", RegistryValueKind.DWord, 1, null),
                new RegistryValueSpec(RegistryHive2.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\DataCollection", "MaxTelemetryAllowed", RegistryValueKind.DWord, 1, null)),
        },

        // ---------------------------------------------------------------- Performance

        new TweakDefinition
        {
            Id = "performance.best-appearance-for-speed",
            Name = "Adjust visual effects for best performance",
            Category = "Performance",
            Tags = ["performance", "visual-effects", "low-spec", "animations"],
            WhatItDoes = "Turns off window and menu animations, fade and slide effects, drag-full-window, listview shadows and Aero Peek. This is the same set as \"Adjust for best performance\" in System Properties > Performance, but it leaves font smoothing on.",
            WhyYouMightWant = "On a slow CPU or GPU, or an old laptop, the desktop feels a lot snappier. Font smoothing stays on so text still looks right.",
            Downside = "The UI is flat and abrupt, with no smooth transitions.",
            Risk = TweakRisk.Low,
            Restart = RestartScope.SignOut,
            Recommended = false,
            Sources = [new Evidence("Explorer\\VisualEffects\\VisualFXSetting = 2 plus the documented per-effect values")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", RegistryValueKind.DWord, 2, 0),
                new RegistryValueSpec(RegistryHive2.CurrentUser, Advanced, "TaskbarAnimations", RegistryValueKind.DWord, 0, 1),
                new RegistryValueSpec(RegistryHive2.CurrentUser, Advanced, "ListviewShadow", RegistryValueKind.DWord, 0, 1),
                new RegistryValueSpec(RegistryHive2.CurrentUser, Advanced, "ListviewAlphaSelect", RegistryValueKind.DWord, 0, 1),
                new RegistryValueSpec(RegistryHive2.CurrentUser, @"Control Panel\Desktop", "DragFullWindows", RegistryValueKind.String, "0", "1"),
                new RegistryValueSpec(RegistryHive2.CurrentUser, @"Control Panel\Desktop\WindowMetrics", "MinAnimate", RegistryValueKind.String, "0", "1"),
                new RegistryValueSpec(RegistryHive2.CurrentUser, @"Software\Microsoft\Windows\DWM", "EnableAeroPeek", RegistryValueKind.DWord, 0, 1)),
        },

        new TweakDefinition
        {
            Id = "performance.disable-transparency",
            Name = "Turn off transparency effects",
            Category = "Performance",
            Tags = ["performance", "transparency", "low-spec"],
            WhatItDoes = "Turns off the acrylic blur on the Start menu, taskbar and other surfaces.",
            WhyYouMightWant = "The GPU recomputes that blur constantly, so turning it off helps weak integrated graphics. Matches \"Transparency effects\" in Settings, under Personalisation > Colours.",
            Downside = "Those surfaces go solid. Purely a look.",
            Risk = TweakRisk.Low,
            Recommended = false,
            Sources = [new Evidence("Themes\\Personalize\\EnableTransparency")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", RegistryValueKind.DWord, 0, 1)),
        },

        new TweakDefinition
        {
            Id = "performance.instant-menus",
            Name = "Remove the menu-open delay",
            Category = "Performance",
            Tags = ["performance", "responsiveness"],
            WhatItDoes = "Sets the menu-show delay to 0 ms. The default is 400 ms.",
            WhyYouMightWant = "Menus and Start feel instant instead of laggy.",
            Downside = "Menus can feel twitchy if you sweep the mouse across them fast.",
            Risk = TweakRisk.Low,
            Restart = RestartScope.SignOut,
            Recommended = false,
            Sources = [new Evidence("Control Panel\\Desktop\\MenuShowDelay")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, @"Control Panel\Desktop", "MenuShowDelay", RegistryValueKind.String, "0", "400")),
        },

        new TweakDefinition
        {
            Id = "performance.no-startup-delay",
            Name = "Remove the startup-app delay",
            Category = "Performance",
            Tags = ["performance", "startup", "boot"],
            WhatItDoes = "Removes the roughly 10-second wait Windows adds before it launches your startup apps after you sign in.",
            WhyYouMightWant = "Your startup apps are ready sooner. Windows adds that wait to make the desktop paint faster first, which you often do not need on a fast disk.",
            Downside = "The desktop may feel a bit busier in the first few seconds after login.",
            Risk = TweakRisk.Low,
            Recommended = false,
            Sources = [new Evidence("Explorer\\Serialize\\StartupDelayInMSec")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize", "StartupDelayInMSec", RegistryValueKind.DWord, 0, null)),
        },

        new TweakDefinition
        {
            Id = "performance.disable-search-indexing",
            Name = "Turn off Windows Search indexing",
            Category = "Performance",
            Tags = ["performance", "search", "disk", "low-spec"],
            WhatItDoes = "Disables the Windows Search service, so it stops indexing your files in the background.",
            WhyYouMightWant = "On a hard drive or a low-RAM machine the indexer is a constant, real load. On a spinning disk this is the biggest Potato mode win there is.",
            Downside = "Search inside File Explorer and Outlook gets much slower, because it falls back to scanning. Start-menu app search is not affected.",
            Risk = TweakRisk.Advanced,
            Privilege = PrivilegeLevel.Administrator,
            Restart = RestartScope.Reboot,
            Recommended = false,
            Sources = [new Evidence("Services\\WSearch\\Start = 4 (disabled)")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.LocalMachine, @"SYSTEM\CurrentControlSet\Services\WSearch", "Start", RegistryValueKind.DWord, 4, 2)),
        },

        // ---------------------------------------------------------------- Security (advanced, trade-offs)
        // These reduce a real Windows protection. They are never Recommended and never part of a
        // built-in profile. Each needs an explicit confirmation and is fully reversible here.

        new TweakDefinition
        {
            Id = "security.disable-smartscreen",
            Name = "Turn off SmartScreen (apps and files, Edge, Store)",
            Category = "Security (advanced)",
            Tags = ["security", "smartscreen", "reputation"],
            WhatItDoes = "Uses policy to turn off the Microsoft Defender SmartScreen reputation check for downloaded programs, for Edge and for Store apps.",
            WhyYouMightWant = "Stops the \"Windows protected your PC\" prompt and the reputation lookup on every new executable. Some developers and privacy-focused users would rather rely on their own judgement plus Defender's on-access scan.",
            Downside = "You lose an early warning against malware and phishing sites that are new. Only sensible if you are careful about what you run and keep another layer, such as Defender real-time or a DNS filter.",
            Risk = TweakRisk.SecurityTradeoff,
            Privilege = PrivilegeLevel.Administrator,
            Restart = RestartScope.SignOut,
            Recommended = false,
            Sources = [new Evidence("Policy System\\EnableSmartScreen = 0; Explorer\\SmartScreenEnabled = Off", "https://learn.microsoft.com/windows/security/operating-system-security/virus-and-threat-protection/microsoft-defender-smartscreen/")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows\System", "EnableSmartScreen", RegistryValueKind.DWord, 0, null),
                new RegistryValueSpec(RegistryHive2.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer", "SmartScreenEnabled", RegistryValueKind.String, "Off", "RequireAdmin"),
                new RegistryValueSpec(RegistryHive2.CurrentUser, @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppHost", "EnableWebContentEvaluation", RegistryValueKind.DWord, 0, 1)),
        },

        new TweakDefinition
        {
            Id = "security.disable-firewall",
            Name = "Turn off the Windows Firewall (all profiles)",
            Category = "Security (advanced)",
            Tags = ["security", "firewall", "network"],
            WhatItDoes = "Sets the Domain, Private and Public firewall profiles to off in the Windows Firewall service config.",
            WhyYouMightWant = "Chasing down a blocked app or game server, or you run a separate hardware or third-party firewall. Turning it back on is one click.",
            Downside = "Every listening service on this PC is now reachable from the local network. Do not do this on public Wi-Fi or any network you do not trust.",
            Risk = TweakRisk.SecurityTradeoff,
            Privilege = PrivilegeLevel.Administrator,
            Recommended = false,
            Sources = [new Evidence("Services\\SharedAccess\\...\\FirewallPolicy\\{Standard,Public,Domain}Profile\\EnableFirewall", "https://learn.microsoft.com/windows/security/operating-system-security/network-security/windows-firewall/")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.LocalMachine, @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\StandardProfile", "EnableFirewall", RegistryValueKind.DWord, 0, 1),
                new RegistryValueSpec(RegistryHive2.LocalMachine, @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\PublicProfile", "EnableFirewall", RegistryValueKind.DWord, 0, 1),
                new RegistryValueSpec(RegistryHive2.LocalMachine, @"SYSTEM\CurrentControlSet\Services\SharedAccess\Parameters\FirewallPolicy\DomainProfile", "EnableFirewall", RegistryValueKind.DWord, 0, 1)),
        },

        new TweakDefinition
        {
            Id = "security.disable-defender-realtime",
            Name = "Turn off Microsoft Defender real-time protection (policy)",
            Category = "Security (advanced)",
            Tags = ["security", "defender", "antivirus"],
            WhatItDoes = "Writes the Group Policy values that disable Microsoft Defender Antivirus and its real-time monitoring. Meant for machines running a different, active antivirus.",
            WhyYouMightWant = "You installed a third-party AV and want Defender fully out of the way, or you are doing malware analysis in an isolated VM.",
            Downside = "If nothing else is protecting the machine, it is now open to file-based malware. On Windows 10 and 11 with Tamper Protection on (the default) these values are ignored until you turn Tamper Protection off by hand in Windows Security. PowerX will not touch that setting for you.",
            Risk = TweakRisk.SecurityTradeoff,
            Privilege = PrivilegeLevel.Administrator,
            Restart = RestartScope.Reboot,
            Recommended = false,
            Sources = [new Evidence("Policy Windows Defender\\DisableAntiSpyware; Real-Time Protection\\DisableRealtimeMonitoring", "https://learn.microsoft.com/microsoft-365/security/defender-endpoint/microsoft-defender-antivirus-windows")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender", "DisableAntiSpyware", RegistryValueKind.DWord, 1, null),
                new RegistryValueSpec(RegistryHive2.LocalMachine, @"SOFTWARE\Policies\Microsoft\Windows Defender\Real-Time Protection", "DisableRealtimeMonitoring", RegistryValueKind.DWord, 1, null)),
        },

        new TweakDefinition
        {
            Id = "security.disable-uac",
            Name = "Turn off User Account Control (UAC)",
            Category = "Security (advanced)",
            Tags = ["security", "uac", "elevation"],
            WhatItDoes = "Sets EnableLUA to 0, which turns UAC off completely. No consent or credential prompts, and every process for an admin account runs with full admin rights.",
            WhyYouMightWant = "A single-user machine where the prompts get in the way, or a legacy line-of-business app that misbehaves under UAC virtualization.",
            Downside = "A big drop in security. Malware that reaches your account runs elevated with no prompt, and file and registry virtualization stop. It also breaks most Store and packaged apps, including parts of Windows, because they need UAC on. Needs a reboot.",
            Risk = TweakRisk.SecurityTradeoff,
            Privilege = PrivilegeLevel.Administrator,
            Restart = RestartScope.Reboot,
            Recommended = false,
            Sources = [new Evidence("Policies\\System\\EnableLUA (1 = on, 0 = off)", "https://learn.microsoft.com/windows/security/application-security/application-control/user-account-control/settings-and-configuration")],
            Operation = new RegistryTweakOperation(
                new RegistryValueSpec(RegistryHive2.LocalMachine, @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System", "EnableLUA", RegistryValueKind.DWord, 0, 1)),
        },
    ];

    private const string WuPolicy = @"SOFTWARE\Policies\Microsoft\Windows\WindowsUpdate";

    private static RegistryValueSpec Cdm(string name) => new(
        RegistryHive2.CurrentUser,
        @"Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
        name, RegistryValueKind.DWord, 0, 1);
}
