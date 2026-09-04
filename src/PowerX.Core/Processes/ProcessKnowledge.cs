namespace PowerX.Core.Processes;

public sealed record ProcessExplanation(string Summary, bool KnownGood, string? Source = null);

/// <summary>
/// A short, plain-language note about a common Windows or third-party process, so "what is this
/// and should I worry about it" has an answer without leaving the app. This is not a verdict on
/// whether a specific running instance is legitimate — a name and a description can be spoofed —
/// it only says what the name normally means. For an actual file, the Security page's hash lookup
/// is the real check.
/// </summary>
public static class ProcessKnowledge
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["System Idle Process"] = "Not a real process. Its \"CPU usage\" is just how idle the machine is.",
        ["System"] = "The Windows kernel itself. Handles I/O and memory that no user-mode process owns.",
        ["Registry"] = "Holds the in-memory registry hives. Normal to see using memory equal to your registry size.",
        ["smss.exe"] = "Session Manager Subsystem. Starts each Windows session at boot. Should have no child after boot finishes.",
        ["csrss.exe"] = "Client/Server Runtime. Core Windows subsystem process, one per session. Never end this.",
        ["wininit.exe"] = "Windows Initialization process. Starts services and other core processes at boot.",
        ["winlogon.exe"] = "Handles sign-in, sign-out and the secure attention sequence (Ctrl+Alt+Del).",
        ["services.exe"] = "The Service Control Manager. Starts, stops and supervises every Windows service.",
        ["lsass.exe"] = "Local Security Authority. Handles sign-in credentials and security tokens. Never end this.",
        ["svchost.exe"] = "A generic host for Windows services grouped into one process. Normal to see many copies; check the Services page to see which services a given one hosts.",
        ["explorer.exe"] = "Windows Explorer: the desktop, taskbar and File Explorer windows.",
        ["dwm.exe"] = "Desktop Window Manager. Renders window transparency, effects and composition. Normal to use noticeable GPU.",
        ["ctfmon.exe"] = "Handles text input, touch keyboard and input-method switching.",
        ["sihost.exe"] = "Shell Infrastructure Host. Supports Start menu, Action Center and other shell UI.",
        ["fontdrvhost.exe"] = "Font-rendering support process for the desktop and logon UI. Normal to see two copies.",
        ["taskhostw.exe"] = "Hosts DLL-based background tasks that Windows schedules.",
        ["RuntimeBroker.exe"] = "Checks app-permission prompts for Store apps. Normal to see several, low CPU/memory.",
        ["conhost.exe"] = "Console Window Host, the window that hosts a command-line app.",
        ["dllhost.exe"] = "A generic host for a COM component running out-of-process (e.g. an Explorer extension).",
        ["spoolsv.exe"] = "Print Spooler. Manages the print queue. Only needed if you print.",
        ["audiodg.exe"] = "Windows Audio Device Graph. Mixes and applies effects to system audio.",
        ["SearchIndexer.exe"] = "Indexes files and email for fast Windows Search. Uses more disk/CPU right after a big file change, then settles.",
        ["SearchHost.exe"] = "The Windows Search UI (Start menu search box).",
        ["StartMenuExperienceHost.exe"] = "Renders the Start menu.",
        ["ShellExperienceHost.exe"] = "Renders parts of the shell UI (Action Center, some Start menu tiles).",
        ["MsMpEng.exe"] = "Microsoft Defender Antivirus's scanning engine. Real-time protection; higher CPU during a scan is normal.",
        ["NisSrv.exe"] = "Microsoft Defender's Network Inspection Service (network-based exploit protection).",
        ["SecurityHealthService.exe"] = "Reports Windows Security status (the shield icon and its notifications).",
        ["SgrmBroker.exe"] = "System Guard Runtime Monitor: platform integrity checks. Low, steady resource use.",
        ["WmiPrvSE.exe"] = "A WMI provider host. Many tools (including PowerX) query WMI through this.",
        ["backgroundTaskHost.exe"] = "Hosts a Store app's background task.",
        ["ApplicationFrameHost.exe"] = "Provides the window frame for Store (UWP) apps.",
        ["TextInputHost.exe"] = "Supports the touch keyboard and emoji panel.",
        ["igfxEM.exe"] = "Intel integrated-graphics tray/helper process.",
        ["nvcontainer.exe"] = "NVIDIA's background service container (telemetry, driver helpers, overlay).",
        ["chrome.exe"] = "Google Chrome. Normal to see several copies — one per tab/extension process, by design.",
        ["msedge.exe"] = "Microsoft Edge. Same multi-process design as Chrome.",
        ["firefox.exe"] = "Mozilla Firefox.",
        ["Discord.exe"] = "Discord chat client.",
        ["Spotify.exe"] = "Spotify music client.",
        ["steam.exe"] = "Steam client / game launcher.",
        ["EpicGamesLauncher.exe"] = "Epic Games launcher.",
        ["OneDrive.exe"] = "Microsoft OneDrive sync client.",
    };

    public static ProcessExplanation Explain(string name, string? imagePath, string? company)
    {
        if (Known.TryGetValue(Path.GetFileNameWithoutExtension(name) + ".exe", out var note) ||
            Known.TryGetValue(name, out note))
            return new ProcessExplanation(note, true, "PowerX");

        if (!string.IsNullOrEmpty(company) &&
            company.Contains("Microsoft", StringComparison.OrdinalIgnoreCase) &&
            IsUnderSystemDirectory(imagePath))
            return new ProcessExplanation(
                "Signed by Microsoft and running from the Windows system folder. Almost certainly a normal Windows component.",
                true, "heuristic");

        if (!string.IsNullOrEmpty(company))
            return new ProcessExplanation(
                $"Not in PowerX's list of common processes, but its file says it is published by {company}. " +
                "If you want a second opinion on the actual file, check its hash on the Security page.",
                false, "file metadata");

        return new ProcessExplanation(
            "Not in PowerX's list, and its file has no publisher information. That alone does not mean " +
            "anything is wrong — plenty of legitimate small tools are unsigned — but if you do not recognise " +
            "it, checking its hash on the Security page is the honest next step.",
            false, null);
    }

    private static bool IsUnderSystemDirectory(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath)) return false;
        try
        {
            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            return imagePath.StartsWith(windir, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}
