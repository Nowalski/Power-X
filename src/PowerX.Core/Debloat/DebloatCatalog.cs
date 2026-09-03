namespace PowerX.Core.Debloat;

public enum RemovalClass
{
    /// <summary>Consumer app most users don't need; safely removable and reinstallable.</summary>
    RecommendedRemovable,
    /// <summary>Legitimate for some; remove if you don't use it.</summary>
    Optional,
    /// <summary>Removing may affect features or is only for advanced users.</summary>
    Advanced,
    /// <summary>Part of the Windows shell / platform — do not remove.</summary>
    KeepSystem,
}

public enum RestoreDifficulty
{
    /// <summary>Reinstall from the Microsoft Store or `winget`.</summary>
    Reinstallable,
    /// <summary>Restore needs an ISO, Feature-on-Demand source, or account steps.</summary>
    Difficult,
}

/// <summary>
/// One curated entry describing a package (or family of packages) and our stance on removing it.
/// Matching is by <see cref="FamilyNameContains"/> (case-insensitive substring of PackageFamilyName).
/// </summary>
public sealed record DebloatEntry(
    string FamilyNameContains,
    string DisplayName,
    string Category,
    string Description,
    RemovalClass Class,
    RestoreDifficulty Restore = RestoreDifficulty.Reinstallable);

/// <summary>
/// The curated debloat catalog. Everything here is a Store/consumer package — no shell
/// components, no Feature-on-Demand, no capabilities. Nothing is preselected for removal.
/// </summary>
public static class DebloatCatalog
{
    public static IReadOnlyList<DebloatEntry> Entries { get; } =
    [
        // ---- Microsoft consumer apps ----
        new("Microsoft.BingNews", "Microsoft News", "Microsoft consumer", "News aggregator app and its live-tile feed.", RemovalClass.RecommendedRemovable),
        new("Microsoft.BingWeather", "Weather (MSN)", "Microsoft consumer", "MSN Weather app.", RemovalClass.RecommendedRemovable),
        new("Microsoft.BingSearch", "Web Search (Bing)", "Microsoft consumer", "The Bing web-search app tied to Start search.", RemovalClass.Optional),
        new("Microsoft.BingFinance", "Money (MSN)", "Microsoft consumer", "MSN Money.", RemovalClass.RecommendedRemovable),
        new("Microsoft.BingSports", "Sports (MSN)", "Microsoft consumer", "MSN Sports.", RemovalClass.RecommendedRemovable),
        new("Microsoft.WindowsFeedbackHub", "Feedback Hub", "Microsoft consumer", "Sends feedback to Microsoft. Only useful for Insiders.", RemovalClass.RecommendedRemovable),
        new("Microsoft.GetHelp", "Get Help", "Microsoft consumer", "Support/help assistant app.", RemovalClass.RecommendedRemovable),
        new("Microsoft.Getstarted", "Tips", "Microsoft consumer", "The 'Tips' app and its notifications.", RemovalClass.RecommendedRemovable),
        new("Microsoft.MicrosoftOfficeHub", "Microsoft 365 (Office) hub", "Microsoft consumer", "Launcher/upsell for Office. Not the Office apps themselves.", RemovalClass.RecommendedRemovable),
        new("Microsoft.MicrosoftSolitaireCollection", "Solitaire Collection", "Games", "Ad-supported card games bundle.", RemovalClass.RecommendedRemovable),
        new("Microsoft.People", "People", "Microsoft consumer", "Contacts aggregator, largely unused on Win11.", RemovalClass.Optional),
        new("Microsoft.windowscommunicationsapps", "Mail and Calendar (old)", "Microsoft consumer", "The legacy Mail/Calendar apps (replaced by new Outlook).", RemovalClass.Optional),
        new("Microsoft.OutlookForWindows", "Outlook (new)", "Microsoft consumer", "The new web-based Outlook client.", RemovalClass.Optional),
        new("MicrosoftTeams", "Microsoft Teams (personal)", "Communication", "The consumer Teams / Chat app.", RemovalClass.Optional),
        new("Microsoft.Todos", "Microsoft To Do", "Productivity", "Task list app.", RemovalClass.Optional),
        new("Microsoft.PowerAutomateDesktop", "Power Automate", "Productivity", "Desktop RPA tool, preinstalled but rarely used.", RemovalClass.Optional),
        new("Clipchamp.Clipchamp", "Clipchamp", "Media", "Microsoft's video editor.", RemovalClass.Optional),
        new("Microsoft.ZuneMusic", "Media Player / Groove", "Media", "The modern Media Player. Removing leaves no built-in music player.", RemovalClass.Advanced),
        new("Microsoft.ZuneVideo", "Movies & TV", "Media", "Video store + local video player.", RemovalClass.Optional),
        new("Microsoft.WindowsMaps", "Maps", "Microsoft consumer", "Offline-capable maps app.", RemovalClass.Optional),
        new("Microsoft.MixedReality.Portal", "Mixed Reality Portal", "Microsoft consumer", "Windows MR portal. Obsolete for most people.", RemovalClass.RecommendedRemovable),
        new("Microsoft.549981C3F5F10", "Cortana", "Microsoft consumer", "The standalone Cortana app.", RemovalClass.RecommendedRemovable),
        new("MicrosoftCorporationII.QuickAssist", "Quick Assist", "Microsoft consumer", "Remote-assistance tool. Keep it if you help others remotely.", RemovalClass.Optional),
        new("Microsoft.Windows.DevHome", "Dev Home", "Developer", "Developer dashboard, preinstalled on some builds.", RemovalClass.Optional),
        new("Microsoft.Windows.Ai.Copilot.Provider", "Copilot provider", "Windows AI", "Backing package for the Copilot experience.", RemovalClass.Advanced, RestoreDifficulty.Difficult),
        new("Microsoft.Copilot", "Copilot", "Windows AI", "The Windows Copilot app.", RemovalClass.Optional),
        new("Microsoft.SkypeApp", "Skype", "Communication", "The preinstalled Skype app. Skype is being retired by Microsoft.", RemovalClass.RecommendedRemovable),
        new("Microsoft.YourPhone", "Phone Link", "Communication", "Links an Android/iPhone to Windows for texts and photos. Remove if you don't pair a phone.", RemovalClass.Optional),
        new("MicrosoftWindows.CrossDevice", "Cross Device", "Communication", "Backing service for Phone Link / 'Link to Windows' and cross-device resume.", RemovalClass.Optional),
        new("Microsoft.Microsoft3DViewer", "3D Viewer", "Legacy 3D", "Views 3D models. Deprecated and removed from newer installs.", RemovalClass.RecommendedRemovable),
        new("Microsoft.MSPaint", "Paint 3D", "Legacy 3D", "The 3D version of Paint. Deprecated. The classic Paint app is separate and stays.", RemovalClass.RecommendedRemovable),
        new("Microsoft.Print3D", "Print 3D", "Legacy 3D", "Sends models to 3D printers. Deprecated.", RemovalClass.RecommendedRemovable),
        new("Microsoft.3DBuilder", "3D Builder", "Legacy 3D", "Old 3D-model creation app.", RemovalClass.RecommendedRemovable),
        new("Microsoft.MicrosoftStickyNotes", "Sticky Notes", "Productivity", "Desktop sticky-note app synced to your account.", RemovalClass.Optional),
        new("Microsoft.WindowsSoundRecorder", "Sound Recorder", "Media", "Voice-memo recorder.", RemovalClass.Optional),
        new("Microsoft.WindowsAlarms", "Clock", "Productivity", "Alarms, timers, world clock and focus sessions.", RemovalClass.Optional),
        new("Microsoft.WindowsCalculator", "Calculator", "Productivity", "The Windows calculator. Removing leaves no built-in calculator.", RemovalClass.Advanced),
        new("Microsoft.Windows.Photos", "Photos", "Media", "The default photo viewer and light editor. Removing leaves no built-in image viewer.", RemovalClass.Advanced),
        new("Microsoft.Wallet", "Wallet", "Microsoft consumer", "Legacy Microsoft Wallet. Does not work on current Windows.", RemovalClass.RecommendedRemovable),
        new("Microsoft.OneConnect", "Mobile Plans", "Microsoft consumer", "Carrier / paid-WiFi sign-up. Only useful on cellular-capable devices.", RemovalClass.Optional),
        new("Microsoft.NetworkSpeedTest", "Network Speed Test", "Microsoft consumer", "Microsoft Research bandwidth tester.", RemovalClass.RecommendedRemovable),
        new("Microsoft.Messaging", "Messaging", "Communication", "Legacy SMS app for old Windows Phone continuity.", RemovalClass.RecommendedRemovable),
        new("MicrosoftWindows.Client.WebExperience", "Widgets board", "Microsoft consumer", "The Win+W widgets/feed panel and the taskbar weather button.", RemovalClass.Optional, RestoreDifficulty.Difficult),

        // ---- Xbox ----
        new("Microsoft.GamingApp", "Xbox app", "Xbox", "The Xbox app / PC Game Pass storefront.", RemovalClass.Optional),
        new("Microsoft.XboxGameOverlay", "Xbox Game Bar overlay", "Xbox", "Overlay component for Game Bar.", RemovalClass.Optional),
        new("Microsoft.XboxGamingOverlay", "Xbox Game Bar", "Xbox", "The Win+G Game Bar. Removing disables its capture/widgets.", RemovalClass.Optional),
        new("Microsoft.XboxIdentityProvider", "Xbox Identity Provider", "Xbox", "Xbox sign-in used by many PC games. Keep it unless you never play.", RemovalClass.Advanced),
        new("Microsoft.XboxSpeechToTextOverlay", "Xbox speech-to-text overlay", "Xbox", "Accessibility overlay for Xbox.", RemovalClass.Optional),
        new("Microsoft.Xbox.TCUI", "Xbox TCUI", "Xbox", "Shared UI used by Xbox in-game invites. Some games need it.", RemovalClass.Advanced),

        // ---- OEM / third-party (matched generically) ----
        new("SpotifyAB.SpotifyMusic", "Spotify", "Third-party", "Preinstalled Spotify stub.", RemovalClass.RecommendedRemovable),
        new("Disney.37853FC22B2CE", "Disney+", "Third-party", "Preinstalled Disney+ stub.", RemovalClass.RecommendedRemovable),
        new("Facebook.Facebook", "Facebook", "Third-party", "Preinstalled Facebook stub.", RemovalClass.RecommendedRemovable),
        new("BytedancePte.Ltd.TikTok", "TikTok", "Third-party", "Preinstalled TikTok stub.", RemovalClass.RecommendedRemovable),
        new("king.com.CandyCrush", "Candy Crush", "Third-party", "Preinstalled King.com game stub(s).", RemovalClass.RecommendedRemovable),
        new("king.com.BubbleWitch", "Bubble Witch Saga", "Third-party", "Preinstalled King.com game stub.", RemovalClass.RecommendedRemovable),
        new("king.com.FarmHeroes", "Farm Heroes Saga", "Third-party", "Preinstalled King.com game stub.", RemovalClass.RecommendedRemovable),
        new("Amazon.com.Amazon", "Amazon", "Third-party", "Preinstalled Amazon Shopping stub.", RemovalClass.RecommendedRemovable),
        new("AmazonVideo.PrimeVideo", "Prime Video", "Third-party", "Preinstalled Prime Video stub.", RemovalClass.RecommendedRemovable),
        new("Netflix", "Netflix", "Third-party", "Preinstalled Netflix stub.", RemovalClass.RecommendedRemovable),
        new("Instagram.Instagram", "Instagram", "Third-party", "Preinstalled Instagram stub.", RemovalClass.RecommendedRemovable),
        new("LinkedInforWindows", "LinkedIn", "Third-party", "Preinstalled LinkedIn stub.", RemovalClass.RecommendedRemovable),
        new("PrestigeXPsMobiles", "OEM promo app", "Third-party", "Common OEM promotional stub.", RemovalClass.RecommendedRemovable),
        new("Microsoft.Whiteboard", "Whiteboard", "Productivity", "Collaborative whiteboard.", RemovalClass.Optional),
        new("Microsoft.Family", "Family", "Microsoft consumer", "Family safety companion app.", RemovalClass.Optional),

        // ---- Media codec extensions (only remove if you never open that format) ----
        new("Microsoft.RawImageExtension", "Raw Image Extension", "Media extensions", "Lets Photos and Explorer read camera RAW files.", RemovalClass.Advanced),
        new("Microsoft.HEIFImageExtension", "HEIF Image Extension", "Media extensions", "Reads .heic photos (common from iPhones).", RemovalClass.Advanced),
        new("Microsoft.WebpImageExtension", "WebP Image Extension", "Media extensions", "Reads .webp images.", RemovalClass.Advanced),
        new("Microsoft.WebMediaExtensions", "Web Media Extensions", "Media extensions", "OGG / Vorbis / Theora playback support.", RemovalClass.Advanced),
        new("Microsoft.VP9VideoExtensions", "VP9 Video Extensions", "Media extensions", "VP9 video decoding (YouTube, WebM).", RemovalClass.Advanced),
        new("Microsoft.HEVCVideoExtension", "HEVC Video Extensions", "Media extensions", "H.265 / HEVC video decoding.", RemovalClass.Advanced),
        new("Microsoft.AV1VideoExtension", "AV1 Video Extension", "Media extensions", "AV1 video decoding.", RemovalClass.Advanced),
        new("Microsoft.MPEG2VideoExtension", "MPEG-2 Video Extension", "Media extensions", "DVD / MPEG-2 video decoding.", RemovalClass.Advanced),

        // ---- Keep: shell / platform ----
        new("Microsoft.WindowsStore", "Microsoft Store", "System", "Removing breaks app installation and updates.", RemovalClass.KeepSystem, RestoreDifficulty.Difficult),
        new("Microsoft.WindowsTerminal", "Windows Terminal", "System", "The default terminal host.", RemovalClass.KeepSystem),
        new("Microsoft.WindowsNotepad", "Notepad", "System", "The default text editor.", RemovalClass.KeepSystem),
        new("Microsoft.Paint", "Paint", "System", "The default image editor.", RemovalClass.KeepSystem),
        new("Microsoft.ScreenSketch", "Snipping Tool", "System", "Screenshot + screen recording tool.", RemovalClass.KeepSystem),
        new("Microsoft.WindowsCamera", "Camera", "System", "The camera app used by many other apps.", RemovalClass.Advanced),
        new("Microsoft.SecHealthUI", "Windows Security", "System", "The Windows Security dashboard. Do not remove.", RemovalClass.KeepSystem, RestoreDifficulty.Difficult),
        new("Microsoft.DesktopAppInstaller", "App Installer (winget)", "System", "Provides winget and .appx install support.", RemovalClass.KeepSystem),
        new("Microsoft.UI.Xaml", "WinUI runtime", "System", "Shared framework many apps depend on.", RemovalClass.KeepSystem),
        new("Microsoft.VCLibs", "VC++ runtime (UWP)", "System", "Shared framework many apps depend on.", RemovalClass.KeepSystem),
        new("Microsoft.NET.Native", ".NET Native runtime", "System", "Shared framework.", RemovalClass.KeepSystem),
        new("Microsoft.WindowsAppRuntime", "Windows App Runtime", "System", "Shared framework for modern apps (incl. PowerX).", RemovalClass.KeepSystem),
        new("Microsoft.StorePurchaseApp", "Store Purchase App", "System", "Handles Store purchases and licensing. Keep it alongside the Store.", RemovalClass.KeepSystem, RestoreDifficulty.Difficult),
        new("Microsoft.GamingServices", "Gaming Services", "System", "Required by the Xbox app and PC Game Pass installs. Keep unless you removed Xbox entirely.", RemovalClass.Advanced, RestoreDifficulty.Difficult),
        new("Microsoft.AccountsControl", "Accounts Control", "System", "System UI for signing into Microsoft/work accounts. Do not remove.", RemovalClass.KeepSystem, RestoreDifficulty.Difficult),
        new("Microsoft.Windows.ShellExperienceHost", "Shell Experience Host", "System", "Renders Start, the taskbar and action center. Do not remove.", RemovalClass.KeepSystem, RestoreDifficulty.Difficult),
        new("Microsoft.Windows.StartMenuExperienceHost", "Start Menu Host", "System", "Renders the Start menu. Do not remove.", RemovalClass.KeepSystem, RestoreDifficulty.Difficult),
        new("Microsoft.AAD.BrokerPlugin", "Work/School Account Broker", "System", "Azure AD / work-account sign-in broker. Do not remove.", RemovalClass.KeepSystem, RestoreDifficulty.Difficult),
    ];

    public static DebloatEntry? Match(string packageFamilyName) =>
        Entries.FirstOrDefault(e => packageFamilyName.Contains(e.FamilyNameContains, StringComparison.OrdinalIgnoreCase));
}
