using PowerX.Core.Diagnostics;
using PowerX.Core.Diagnostics.Crash;
using PowerX.Core.Processes;
using PowerX.Core.Startup;
using PowerX.Core.Telemetry;

namespace PowerX.App.Services;

/// <summary>
/// Synthetic data for documentation screenshots. Enabled only when the
/// <c>POWERX_DEMO</c> environment variable is <c>1</c> or <c>true</c>. It swaps the process
/// list, the telemetry values and the crash list for a fixed, believable set that contains no
/// real machine or user detail. It never touches any code path that changes system state.
/// </summary>
internal static class DemoData
{
    public static bool Active { get; } =
        Environment.GetEnvironmentVariable("POWERX_DEMO") is "1" or "true" or "TRUE";

    // ---- telemetry ---------------------------------------------------------

    private static double Wave(long tick, double baseline, double amp, double periodTicks, double phase) =>
        Math.Clamp(baseline + amp * Math.Sin((tick / periodTicks + phase) * 2 * Math.PI), 0, 100);

    public static CpuMetrics Cpu(long tick)
    {
        double total = Wave(tick, 27, 9, 23, 0) + Wave(tick, 0, 4, 6.5, 0.3);
        total = Math.Clamp(total, 4, 96);
        var cores = new double[16];
        for (int i = 0; i < cores.Length; i++)
            cores[i] = Math.Clamp(Wave(tick + i * 5, total, 22, 7 + i % 4, i * 0.11), 1, 100);
        return new CpuMetrics
        {
            TotalUsagePercent = total,
            KernelUsagePercent = total * 0.28,
            PerLogicalProcessor = cores,
            ProcessCount = Base.Length,
            ThreadCount = Base.Sum(r => r.Threads),
            HandleCount = Base.Sum(r => r.Handles),
            Uptime = TimeSpan.FromHours(6.4),
            Timestamp = DateTimeOffset.Now,
        };
    }

    /// <summary>The real installed RAM, so the demo memory card matches the rest of the UI.</summary>
    public static ulong RealTotalRam { private get; set; } = 34_312_484_864;

    public static MemoryMetrics Memory(long tick)
    {
        ulong total = RealTotalRam > 0 ? RealTotalRam : 34_312_484_864;
        double usedPct = Wave(tick, 41, 3, 40, 0);
        ulong inUse = (ulong)(total * usedPct / 100.0);
        return new MemoryMetrics
        {
            TotalPhysical = total,
            AvailablePhysical = total - inUse,
            UsedPercent = usedPct,
            CachedApprox = (ulong)(total * 0.12),
            CommitTotal = inUse + 4_600_000_000,
            CommitLimit = total + 9_000_000_000,
            PagedPool = 720_000_000,
            NonPagedPool = 540_000_000,
            Timestamp = DateTimeOffset.Now,
        };
    }

    public static GpuMetrics Gpu(long tick)
    {
        double util = Math.Clamp(
            Wave(tick, 22, 12, 19, 0.5) + Wave(tick, 0, 5, 4.3, 0.2) + Wave(tick, 0, 3, 2.1, 0.7),
            2, 100);
        return new GpuMetrics
        {
            UtilizationPercent = util,
            Engines =
            [
                new("3D", util),
                new("Copy", Math.Clamp(util * 0.2, 0, 100)),
                new("Video Decode", Math.Clamp(Wave(tick, 6, 6, 11, 0), 0, 100)),
                new("Compute", Math.Clamp(util * 0.4, 0, 100)),
            ],
            DedicatedMemoryUsed = 4_700_000_000,
            SharedMemoryUsed = 900_000_000,
            Timestamp = DateTimeOffset.Now,
        };
    }

    public static NetworkMetrics Network(long tick)
    {
        double down = 380_000 + 260_000 * Math.Abs(Math.Sin(tick / 9.0));
        double up = 60_000 + 40_000 * Math.Abs(Math.Sin(tick / 13.0 + 1));
        var nic = new NetworkInterfaceMetrics
        {
            Name = "Ethernet",
            Description = "Realtek Gaming 2.5GbE Family Controller",
            Type = "Ethernet",
            IsUp = true,
            LinkSpeedBps = 2_500_000_000,
            SendBytesPerSec = up,
            ReceiveBytesPerSec = down,
            TotalBytesSent = 4_200_000_000,
            TotalBytesReceived = 61_000_000_000,
            MacAddress = "00:00:5E:00:53:2A",
            IpAddresses = ["192.0.2.24"],
            Gateways = ["192.0.2.1"],
            DnsServers = ["192.0.2.1"],
        };
        return new NetworkMetrics([nic], DateTimeOffset.Now);
    }

    // ---- processes -------------------------------------------------------

    private sealed record Row(string Name, double Cpu, double WsMb, int Threads, int Handles);

    // A believable modern-Windows set. Names only, nothing machine-specific.
    private static readonly Row[] Base =
    [
        new("System", 0.4, 0.15, 240, 4200),
        new("Registry", 0.0, 96, 4, 700),
        new("smss.exe", 0.0, 1.1, 2, 55),
        new("csrss.exe", 0.1, 5.6, 12, 720),
        new("wininit.exe", 0.0, 6.3, 3, 150),
        new("services.exe", 0.3, 12.4, 9, 900),
        new("lsass.exe", 0.2, 22.7, 13, 1500),
        new("svchost.exe", 0.6, 44.1, 34, 2100),
        new("svchost.exe", 0.1, 18.9, 16, 780),
        new("svchost.exe", 0.2, 27.3, 22, 1120),
        new("svchost.exe", 0.0, 9.4, 8, 340),
        new("dwm.exe", 3.1, 168.0, 24, 1650),
        new("explorer.exe", 1.7, 214.5, 78, 3200),
        new("StartMenuExperienceHost.exe", 0.0, 74.2, 39, 980),
        new("SearchHost.exe", 0.4, 189.6, 55, 1700),
        new("TextInputHost.exe", 0.0, 41.8, 18, 560),
        new("ShellExperienceHost.exe", 0.0, 66.1, 30, 820),
        new("RuntimeBroker.exe", 0.1, 33.0, 12, 640),
        new("SystemSettings.exe", 0.0, 88.4, 27, 900),
        new("MsMpEng.exe", 2.4, 296.0, 44, 2600),
        new("NisSrv.exe", 0.0, 12.9, 9, 320),
        new("SecurityHealthService.exe", 0.0, 15.7, 10, 360),
        new("audiodg.exe", 0.5, 24.3, 12, 380),
        new("fontdrvhost.exe", 0.0, 7.2, 5, 120),
        new("WmiPrvSE.exe", 0.3, 28.6, 13, 700),
        new("SearchIndexer.exe", 0.8, 112.4, 30, 1400),
        new("chrome.exe", 9.6, 388.0, 32, 1900),
        new("chrome.exe", 3.2, 214.7, 18, 720),
        new("chrome.exe", 1.1, 176.3, 15, 540),
        new("chrome.exe", 0.4, 132.9, 13, 430),
        new("Code.exe", 5.8, 604.2, 46, 2100),
        new("Code.exe", 2.1, 288.5, 22, 760),
        new("Code.exe", 12.7, 512.0, 28, 900),
        new("Spotify.exe", 1.3, 226.8, 40, 980),
        new("Spotify.exe", 0.0, 74.1, 14, 330),
        new("Teams.exe", 2.6, 402.5, 41, 1500),
        new("steam.exe", 0.7, 158.0, 33, 1200),
        new("steamwebhelper.exe", 1.9, 344.6, 27, 890),
        new("obs64.exe", 4.4, 366.1, 38, 1300),
        new("PowerX.App.exe", 1.2, 142.0, 24, 620),
        new("powershell.exe", 0.1, 74.6, 12, 520),
        new("WindowsTerminal.exe", 0.6, 96.3, 20, 640),
        new("OneDrive.exe", 0.3, 118.7, 29, 1100),
        new("nvcontainer.exe", 0.2, 62.4, 22, 780),
        new("igfxCUIService.exe", 0.0, 9.1, 6, 160),
        new("taskhostw.exe", 0.0, 21.5, 11, 420),
        new("svchost.exe", 0.1, 14.2, 11, 430),
        new("svchost.exe", 0.0, 8.7, 7, 260),
        new("svchost.exe", 0.2, 31.6, 19, 900),
        new("svchost.exe", 0.0, 12.1, 9, 350),
        new("svchost.exe", 0.1, 19.4, 14, 540),
        new("svchost.exe", 0.0, 7.9, 6, 210),
        new("svchost.exe", 0.3, 40.5, 26, 1300),
        new("svchost.exe", 0.0, 11.3, 8, 300),
        new("conhost.exe", 0.0, 8.4, 4, 90),
        new("dllhost.exe", 0.0, 13.9, 9, 260),
        new("sihost.exe", 0.0, 26.7, 14, 620),
        new("ctfmon.exe", 0.0, 17.2, 10, 380),
        new("SearchProtocolHost.exe", 0.1, 22.8, 8, 340),
        new("SearchFilterHost.exe", 0.0, 9.6, 6, 160),
        new("spoolsv.exe", 0.0, 16.4, 12, 480),
        new("SgrmBroker.exe", 0.0, 6.8, 5, 130),
        new("wlanext.exe", 0.0, 5.2, 4, 110),
        new("ChsIME.exe", 0.0, 14.1, 8, 220),
        new("backgroundTaskHost.exe", 0.0, 34.9, 12, 380),
        new("smartscreen.exe", 0.0, 28.3, 10, 350),
        new("ApplicationFrameHost.exe", 0.0, 30.1, 13, 470),
        new("PhoneExperienceHost.exe", 0.0, 96.4, 27, 700),
        new("WidgetService.exe", 0.0, 58.2, 20, 520),
        new("Widgets.exe", 0.0, 120.7, 33, 810),
        new("chrome.exe", 0.6, 148.2, 14, 470),
        new("chrome.exe", 0.2, 121.0, 13, 410),
        new("chrome.exe", 0.9, 203.5, 16, 560),
        new("Discord.exe", 0.4, 214.0, 38, 900),
        new("Discord.exe", 0.0, 88.6, 20, 420),
        new("EpicGamesLauncher.exe", 0.3, 268.4, 44, 1100),
        new("NVIDIA Web Helper.exe", 0.0, 41.2, 15, 360),
        new("nvsphelper64.exe", 0.0, 12.7, 8, 190),
        new("RtkAudUService64.exe", 0.0, 9.4, 7, 170),
        new("LightingService.exe", 0.1, 46.8, 22, 640),
        new("armsvc.exe", 0.0, 6.1, 4, 120),
        new("jusched.exe", 0.0, 7.3, 5, 140),
        new("GoogleCrashHandler.exe", 0.0, 3.9, 3, 80),
        new("cmd.exe", 0.0, 4.6, 2, 60),
        new("MoUsoCoreWorker.exe", 0.0, 18.9, 11, 420),
        new("uhssvc.exe", 0.0, 5.7, 4, 110),
        new("TabTip.exe", 0.0, 21.3, 9, 300),
        new("ONENOTEM.EXE", 0.0, 8.8, 5, 150),
    ];

    private static readonly HashSet<string> SystemNames =
        ["System", "Registry", "smss.exe", "csrss.exe", "wininit.exe", "services.exe", "lsass.exe", "fontdrvhost.exe"];

    private static readonly HashSet<string> ThirdParty =
        ["chrome.exe", "Code.exe", "Spotify.exe", "steam.exe", "steamwebhelper.exe", "obs64.exe", "Teams.exe", "nvcontainer.exe"];

    public static ProcessSnapshot Processes(long tick)
    {
        var list = new List<ProcessInfo>(Base.Length);
        int pid = 4;
        for (int i = 0; i < Base.Length; i++)
        {
            var r = Base[i];
            pid += 4 + (i * 37 % 60);
            bool system = SystemNames.Contains(r.Name);
            // gently animate the CPU figure so the charts look live, but keep the sort order stable
            double jitter = 1 + 0.12 * Math.Sin((tick + i * 3) / 6.0);
            double cpu = r.Cpu <= 0 ? 0 : Math.Clamp(r.Cpu * jitter, 0.05, 98);
            list.Add(new ProcessInfo
            {
                Pid = pid,
                ParentPid = i < 6 ? 0 : 4 + (i % 5) * 40,
                Name = r.Name,
                SessionId = system ? 0 : 1,
                ThreadCount = r.Threads,
                HandleCount = r.Handles,
                BasePriority = 8,
                CpuPercent = cpu,
                WorkingSetBytes = (ulong)(r.WsMb * 1024 * 1024),
                PrivateBytes = (ulong)(r.WsMb * 1024 * 1024 * 0.72),
                IoBytesPerSec = cpu > 1 ? cpu * 240_000 : 0,
                HardFaultDelta = 0,
                StartTime = DateTimeOffset.Now.AddHours(-6).AddMinutes(i),
                TotalProcessorTime = TimeSpan.FromSeconds(r.Cpu * 90 + 20),
                UserName = system ? "SYSTEM" : "user",
                Signature = ThirdParty.Contains(r.Name) ? SignatureStatus.TrustedPublisher : SignatureStatus.MicrosoftSigned,
            });
        }
        return new ProcessSnapshot(list, DateTimeOffset.Now, list.Count, list.Sum(p => p.ThreadCount));
    }

    // ---- connections ---------------------------------------------------

    private static NetworkConnection Est(string proc, int lport, string raddr, int rport) => new()
    {
        Protocol = "TCP", Pid = 1000, ProcessName = proc,
        LocalAddress = "192.0.2.24", LocalPort = lport,
        RemoteAddress = raddr, RemotePort = rport,
        State = "ESTABLISHED", IsListening = false, Exposed = false,
    };

    private static NetworkConnection Listen(string proc, int port, string bound) => new()
    {
        Protocol = "TCP", Pid = 900, ProcessName = proc,
        LocalAddress = bound, LocalPort = port,
        RemoteAddress = null, RemotePort = 0,
        State = "LISTEN", IsListening = true,
        Exposed = !(bound.StartsWith("127.") || bound == "::1"),
    };

    public static IReadOnlyList<NetworkConnection> Connections() =>
    [
        Est("chrome.exe", 51840, "142.250.72.196", 443),
        Est("chrome.exe", 51841, "142.250.72.196", 443),
        Est("chrome.exe", 51852, "104.18.32.47", 443),
        Est("chrome.exe", 51877, "151.101.1.140", 443),
        Est("Code.exe", 52010, "20.190.190.1", 443),
        Est("Code.exe", 52044, "140.82.113.25", 443),
        Est("Teams.exe", 52101, "52.113.194.132", 443),
        Est("Teams.exe", 52102, "13.107.42.14", 443),
        Est("Spotify.exe", 52140, "35.186.224.25", 443),
        Est("steam.exe", 27036, "155.133.248.53", 27021),
        Est("Discord.exe", 52200, "162.159.128.233", 443),
        Est("OneDrive.exe", 52233, "13.107.42.12", 443),
        Est("PowerX.App.exe", 52990, "185.199.108.133", 443),
        Listen("svchost.exe", 135, "0.0.0.0"),
        Listen("System", 445, "0.0.0.0"),
        Listen("svchost.exe", 5040, "0.0.0.0"),
        Listen("svchost.exe", 7680, "0.0.0.0"),   // Delivery Optimization
        Listen("spoolsv.exe", 49664, "0.0.0.0"),
        Listen("steam.exe", 27060, "0.0.0.0"),
        Listen("Code.exe", 6463, "127.0.0.1"),
        Listen("Discord.exe", 6463, "127.0.0.1"),
        Listen("PowerX.App.exe", 51999, "127.0.0.1"),
        new()
        {
            Protocol = "TCP", Pid = 1200, ProcessName = "SearchHost.exe",
            LocalAddress = "192.0.2.24", LocalPort = 52310,
            RemoteAddress = "204.79.197.200", RemotePort = 443,
            State = "TIME-WAIT", IsListening = false, Exposed = false,
        },
        new()
        {
            Protocol = "UDP", Pid = 1400, ProcessName = "svchost.exe",
            LocalAddress = "0.0.0.0", LocalPort = 53,
            RemoteAddress = null, RemotePort = 0,
            State = "", IsListening = true, Exposed = true,
        },
        new()
        {
            Protocol = "UDP", Pid = 1400, ProcessName = "svchost.exe",
            LocalAddress = "0.0.0.0", LocalPort = 5353,
            RemoteAddress = null, RemotePort = 0,
            State = "", IsListening = true, Exposed = true,
        },
    ];

    // ---- startup / boot ----------------------------------------------

    public static IReadOnlyList<StartupEntry> StartupEntries() =>
    [
        new() { Name = "OneDrive", Command = "\"C:\\Program Files\\Microsoft OneDrive\\OneDrive.exe\" /background", Source = StartupSource.RunUser, Enabled = true, Publisher = "Microsoft Corporation", ExecutablePath = @"C:\Program Files\Microsoft OneDrive\OneDrive.exe" },
        new() { Name = "Discord", Command = "C:\\Users\\user\\AppData\\Local\\Discord\\Update.exe --processStart Discord.exe", Source = StartupSource.RunUser, Enabled = true, Publisher = "Discord Inc.", ExecutablePath = @"C:\Users\user\AppData\Local\Discord\app-1.0.9\Discord.exe" },
        new() { Name = "Spotify", Command = "C:\\Users\\user\\AppData\\Roaming\\Spotify\\Spotify.exe --autostart --minimized", Source = StartupSource.RunUser, Enabled = true, Publisher = "Spotify AB", ExecutablePath = @"C:\Users\user\AppData\Roaming\Spotify\Spotify.exe" },
        new() { Name = "Steam", Command = "\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", Source = StartupSource.RunUser, Enabled = true, Publisher = "Valve Corporation", ExecutablePath = @"C:\Program Files (x86)\Steam\steam.exe" },
        new() { Name = "EpicGamesLauncher", Command = "\"C:\\Program Files (x86)\\Epic Games\\Launcher\\Portal\\Binaries\\Win64\\EpicGamesLauncher.exe\" -silent", Source = StartupSource.RunUser, Enabled = false, Publisher = "Epic Games, Inc.", ExecutablePath = @"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe" },
        new() { Name = "SecurityHealth", Command = "C:\\WINDOWS\\system32\\SecurityHealthSystray.exe", Source = StartupSource.RunMachine, Enabled = true, Publisher = "Microsoft Corporation", ExecutablePath = @"C:\WINDOWS\system32\SecurityHealthSystray.exe" },
        new() { Name = "RtkAudUService", Command = "\"C:\\WINDOWS\\System32\\DriverStore\\FileRepository\\realtekservice\\RtkAudUService64.exe\" -background", Source = StartupSource.RunMachine, Enabled = true, Publisher = "Realtek Semiconductor", ExecutablePath = @"C:\WINDOWS\System32\RtkAudUService64.exe" },
        new() { Name = "NVIDIA App", Command = "\"C:\\Program Files\\NVIDIA Corporation\\NVIDIA App\\CEF\\NVIDIA app.exe\" --start-minimized", Source = StartupSource.RunMachine, Enabled = true, Publisher = "NVIDIA Corporation", ExecutablePath = @"C:\Program Files\NVIDIA Corporation\NVIDIA App\CEF\NVIDIA app.exe" },
        new() { Name = "Adobe GC Invoker Utility", Command = "\"C:\\Program Files (x86)\\Common Files\\Adobe\\AdobeGCClient\\AGCInvokerUtility.exe\"", Source = StartupSource.RunMachine, Enabled = false, Publisher = "Adobe Inc.", ExecutablePath = @"C:\Program Files (x86)\Common Files\Adobe\AdobeGCClient\AGCInvokerUtility.exe" },
        new() { Name = "Backup and Sync", Command = "\"C:\\Program Files\\Google\\Drive File Stream\\launch.bat\"", Source = StartupSource.StartupFolderUser, Enabled = true, Publisher = "Google LLC", ExecutablePath = @"C:\Program Files\Google\Drive File Stream\GoogleDriveFS.exe" },
        new() { Name = "OneDrive per-machine standalone updater", Command = "\"C:\\Program Files (x86)\\Microsoft OneDrive\\StandaloneUpdater\\OneDriveSetup.exe\" /update", Source = StartupSource.ScheduledTask, Enabled = true, Publisher = "Microsoft Corporation", TaskPath = @"\Microsoft\OneDrive\Standalone Update Task-S-1-5-21" },
    ];

    public static BootTimeline BootTimeline()
    {
        int[] recent = [41_600, 33_200, 35_800, 31_900, 34_100, 30_700, 36_400, 33_500, 32_800, 38_100, 34_900, 31_200];
        var now = DateTimeOffset.Now;
        return new()
        {
            LastBootWhen = now.AddHours(-6),
            LastBootMs = recent[0],
            MainPathMs = 28_400,
            AverageBootMs = 34_900,
            StartupAppCount = 11,
            Degraded = true,
            Recent = recent.Select((ms, i) => new BootRecord(now.AddDays(-i).AddHours(-6), ms, (int)(ms * 0.68))).ToList(),
        };
    }

    public static IReadOnlyList<BootItem> BootItems() =>
    [
        new() { Name = "EpicGamesLauncher", Path = @"C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe", Kind = BootItemKind.App, TotalMs = 4200, DegradationMs = 2600, When = DateTimeOffset.Now.AddHours(-6) },
        new() { Name = "Discord", Path = @"C:\Users\user\AppData\Local\Discord\app-1.0.9\Discord.exe", Kind = BootItemKind.App, TotalMs = 2900, DegradationMs = 1400, When = DateTimeOffset.Now.AddHours(-6) },
        new() { Name = "Spotify", Path = @"C:\Users\user\AppData\Roaming\Spotify\Spotify.exe", Kind = BootItemKind.App, TotalMs = 1600, DegradationMs = 600, When = DateTimeOffset.Now.AddHours(-6) },
        new() { Name = "OneDrive", Path = @"C:\Program Files\Microsoft OneDrive\OneDrive.exe", Kind = BootItemKind.App, TotalMs = 900, DegradationMs = 240, When = DateTimeOffset.Now.AddHours(-6) },
    ];

    // ---- security ------------------------------------------------------

    public static DefenderStatus DefenderStatus() => new()
    {
        Mode = DefenderMode.Normal,
        ModeText = "Microsoft Defender is the active antivirus",
        RealTimeProtection = true,
        CloudProtection = true,
        BehaviorMonitor = true,
        TamperProtection = true,
        NetworkProtection = true,
        PuaProtection = "on",
        SignatureVersion = "1.421.88.0",
        SignatureUpdated = DateTimeOffset.Now.AddHours(-5),
        SignatureAgeDays = 0,
        LastQuickScan = DateTimeOffset.Now.AddHours(-14),
        LastFullScan = DateTimeOffset.Now.AddDays(-6),
        ExclusionCount = 2,
    };

    public static IReadOnlyList<DefenderThreat> DefenderThreats() =>
    [
        new()
        {
            Name = "Trojan:Win32/Wacatac.B!ml", Severity = "Severe",
            When = DateTimeOffset.Now.AddDays(-19).AddHours(-2),
            State = DefenderThreatState.Removed, DidExecute = false, Active = false,
            Resource = "C:\\Users\\user\\Downloads\\setup_x64_1042.exe",
        },
        new()
        {
            Name = "PUA:Win32/Presenoker", Severity = "Low",
            When = DateTimeOffset.Now.AddDays(-41).AddHours(-6),
            State = DefenderThreatState.Quarantined, DidExecute = false, Active = false,
            Resource = "C:\\Users\\user\\AppData\\Local\\Temp\\driver-booster-setup.exe",
        },
    ];

    // ---- crashes --------------------------------------------------------

    public static IReadOnlyList<CrashInsight> Crashes()
    {
        var now = DateTimeOffset.Now;
        return
        [
            new CrashInsight
            {
                Source = "Event 1001 (bugcheck)",
                Subject = "Stop error 0x116 (VIDEO_TDR_FAILURE)",
                Kind = CrashKind.Bugcheck,
                When = now.AddDays(-2).AddHours(-3),
                Confidence = CrashConfidence.Moderate,
                Facts =
                [
                    "The system stopped with bugcheck 0x116, VIDEO_TDR_FAILURE.",
                    "Parameter 2 points at the display driver dxgkrnl.sys.",
                    "A crash dump was written to C:\\Windows\\MEMORY.DMP (18 MB).",
                ],
                LikelyCauses =
                [
                    "The graphics driver stopped responding and Windows could not reset it. This usually means an unstable driver, a GPU overclock, or a hardware fault under load.",
                ],
                Remediation =
                [
                    "Do a clean reinstall of the current graphics driver.",
                    "Remove any GPU overclock or undervolt and test again.",
                    "Watch GPU temperatures under load.",
                ],
                Missing = ["A full analysis needs the kernel dump and symbols, which PowerX does not load."],
            },
            new CrashInsight
            {
                Source = "Windows Error Reporting",
                Subject = "PhotoViewer.exe stopped responding",
                Kind = CrashKind.AppHang,
                When = now.AddDays(-1).AddHours(-6),
                Confidence = CrashConfidence.Low,
                Facts =
                [
                    "PhotoViewer.exe was reported as not responding for 12 seconds, then closed.",
                    "The hang thread was waiting on a file open on drive D:.",
                ],
                LikelyCauses =
                [
                    "The app blocked the UI thread while reading from a slow or disconnected drive.",
                ],
                Remediation =
                [
                    "Check that drive D: is healthy and connected.",
                    "Update the app to its latest version.",
                ],
                Missing = ["Only the hang report is available. There is no dump to confirm the stack."],
            },
            new CrashInsight
            {
                Source = "Event 1026 (.NET Runtime)",
                Subject = "A .NET app crashed with an unhandled exception",
                Kind = CrashKind.ManagedException,
                When = now.AddHours(-5),
                Confidence = CrashConfidence.High,
                Facts =
                [
                    "SampleTool.exe raised System.IO.FileNotFoundException and did not handle it.",
                    "The message names a config file under the app folder.",
                    "The stack starts in SampleTool.Startup.LoadConfig.",
                ],
                LikelyCauses =
                [
                    "The app expected a configuration file that was missing or in the wrong place, and had no fallback.",
                ],
                Remediation =
                [
                    "Reinstall or repair the app so its files are complete.",
                    "If you moved the app folder, put it back or reinstall.",
                ],
                Missing = [],
            },
        ];
    }

    // ---- tools: pending reboot / component store / battery -----------

    public static PendingRebootStatus PendingReboot() => new(true,
    [
        "Windows Update installed something that needs a restart.",
        "3 file(s) are queued to be replaced or deleted on restart (usually a program that updated files still in use).",
    ]);

    public static ComponentStoreInfo ComponentStore() => new()
    {
        ActualSizeBytes = 8_150_000_000,
        SharedWithWindowsBytes = 5_900_000_000,
        BackupsAndDisabledBytes = 1_780_000_000,
        CacheAndTempBytes = 470_000_000,
        ReclaimablePackages = 12,
        CleanupRecommended = true,
        LastCleanup = DateTimeOffset.Now.AddDays(-34),
    };

    public static BatteryInfo Battery() => new()
    {
        HasBattery = true,
        Manufacturer = "LGC",
        Chemistry = "LiP",
        DesignCapacityMwh = 60_000,
        FullChargeCapacityMwh = 51_200,
        CycleCount = 214,
        ChargePercent = 74,
        OnAcPower = false,
        Charging = false,
        EstimatedRuntime = TimeSpan.FromMinutes(212),
        FullChargeRuntime = TimeSpan.FromMinutes(305),
    };

    // ---- what changed ----------------------------------------------

    public static SnapshotDiff SnapshotDiff()
    {
        var now = DateTimeOffset.Now;
        return new SnapshotDiff(now.AddDays(-7), now,
        [
            new(SnapshotCategory.Startup, "Spotify", ChangeKind.Added, null, "enabled"),
            new(SnapshotCategory.Startup, "EpicGamesLauncher", ChangeKind.Changed, "enabled", "disabled"),
            new(SnapshotCategory.ScheduledTask, "\\GoogleUpdateTaskMachineUA", ChangeKind.Added, null, "enabled"),
            new(SnapshotCategory.Service, "Razer Chroma SDK Service", ChangeKind.Added, null, "Automatic"),
            new(SnapshotCategory.Program, "Discord", ChangeKind.Changed, "1.0.9038", "1.0.9041"),
            new(SnapshotCategory.Program, "Steam", ChangeKind.Added, null, "installed"),
            new(SnapshotCategory.Program, "CCleaner", ChangeKind.Removed, "6.21", null),
            new(SnapshotCategory.Driver, "NVIDIA GeForce RTX 4070 (NVIDIA)", ChangeKind.Changed, "32.0.15.5599", "32.0.15.6094"),
            new(SnapshotCategory.Tweak, "Show file extensions", ChangeKind.Added, null, "applied"),
        ]);
    }

    // ---- health check --------------------------------------------

    public static HealthReport HealthReport()
    {
        Recommendation R(string cat, string title, string detail, RecommendationImpact impact, string tag, string label) =>
            new() { Category = cat, Title = title, Detail = detail, Impact = impact, NavigateTag = tag, NavigateLabel = label };

        var items = new List<Recommendation>
        {
            R("Restart", "A restart is pending", "Windows Update installed something that needs a restart.", RecommendationImpact.High, "tools", "Open Tools"),
            R("Firewall", "1 broad inbound rule worth a look", "An enabled rule allows any program in over the public network on a specific port.", RecommendationImpact.Medium, "firewall", "Open Firewall"),
            R("Event log", "1 critical event in the last 7 days", "Often an unexpected shutdown or a serious driver fault.", RecommendationImpact.Medium, "events", "Open Event log"),
            R("Drivers", "2 drivers are five years old or more", "Worth checking the vendor for a newer version.", RecommendationImpact.Medium, "drivers", "Open Drivers"),
            R("Startup", "1 startup app measured as high boot impact", "Consider disabling or delaying the slowest ones.", RecommendationImpact.Low, "startup", "Open Startup"),
            R("Startup", "1 startup entry points at a missing program", "Left behind by an app that was removed without cleaning up after itself. Safe to remove.", RecommendationImpact.Low, "startup", "Open Startup"),
            R("Scheduled tasks", "3 telemetry tasks enabled", "Reporting tasks you can safely turn off if you would rather they didn't run.", RecommendationImpact.Low, "tasks", "Open Scheduled tasks"),
            R("Tweaks", "2 recommended tweaks are not applied", "Conservative, broadly safe changes you have not turned on yet.", RecommendationImpact.Low, "tweaks", "Open Tweaks"),
        };
        return new PowerX.Core.Diagnostics.HealthReport { When = DateTimeOffset.Now, Items = items, Deep = false };
    }

    // ---- per-process network -----------------------------------

    public static IReadOnlyList<PowerX.App.Views.ProcNetVm> ProcNet() =>
    [
        new("chrome.exe", "4.8 MB/s", "312 KB/s", "1.9 GB"),
        new("steam.exe", "22.1 MB/s", "180 KB/s", "6.2 GB"),
        new("Spotify.exe", "196 KB/s", "12 KB/s", "84 MB"),
        new("Discord.exe", "44 KB/s", "38 KB/s", "121 MB"),
        new("svchost.exe", "8 KB/s", "2 KB/s", "9 MB"),
        new("OneDrive.exe", "0/s", "410 KB/s", "77 MB"),
    ];

    // ---- drivers --------------------------------------------------

    public static IReadOnlyList<DriverEntry> Drivers()
    {
        DateTimeOffset Ago(int months) => DateTimeOffset.Now.AddMonths(-months);
        return
        [
            new() { Device = "NVIDIA GeForce RTX 4070", Version = "32.0.15.6094", Date = Ago(2), Provider = "NVIDIA", DeviceClass = "Display", Signed = true },
            new() { Device = "Realtek PCIe GbE Family Controller", Version = "10.68.0620.2024", Date = Ago(8), Provider = "Realtek", DeviceClass = "Net", Signed = true },
            new() { Device = "Intel Wi-Fi 6E AX211 160MHz", Version = "23.60.1.2", Date = Ago(5), Provider = "Intel", DeviceClass = "Net", Signed = true },
            new() { Device = "Samsung NVMe SSD Controller", Version = "3.3.0.2003", Date = Ago(41), Provider = "Samsung", DeviceClass = "SCSIAdapter", Signed = true },
            new() { Device = "Realtek High Definition Audio", Version = "6.0.9502.1", Date = Ago(19), Provider = "Realtek Semiconductor Corp.", DeviceClass = "MEDIA", Signed = true },
            new() { Device = "Logitech G HUB Mouse Filter", Version = "1.2.4.0", Date = Ago(63), Provider = "Logitech", DeviceClass = "HIDClass", Signed = false },
            new() { Device = "AMD SMBus", Version = "5.12.0.38", Date = Ago(37), Provider = "Advanced Micro Devices, Inc", DeviceClass = "System", Signed = true },
            new() { Device = "Standard NVM Express Controller", Version = "10.0.26100.1", Date = Ago(6), Provider = "Microsoft", DeviceClass = "SCSIAdapter", Signed = true },
            new() { Device = "USB Composite Device", Version = "10.0.26100.1", Date = Ago(6), Provider = "Microsoft", DeviceClass = "USB", Signed = true },
        ];
    }

    // ---- scheduled tasks ----------------------------------------

    public static IReadOnlyList<ScheduledTaskInfo> ScheduledTasks()
    {
        ScheduledTaskInfo T(string folder, string name, bool on, string action, string triggers, TaskStance stance)
        {
            var s = ScheduledTaskCatalog.StanceFor(folder + "\\" + name, out var note);
            return new ScheduledTaskInfo
            {
                Path = folder + "\\" + name, Name = name, Folder = folder, Enabled = on,
                Action = action, Triggers = triggers,
                LastRun = DateTimeOffset.Now.AddHours(-9),
                Stance = stance, StanceNote = note ?? StanceNote(stance),
            };
        }
        return
        [
            T(@"\Microsoft\Windows\Customer Experience Improvement Program", "Consolidator", true, "%windir%\\system32\\wsqmcons.exe", "daily", TaskStance.Telemetry),
            T(@"\Microsoft\Windows\Application Experience", "Microsoft Compatibility Appraiser", true, "compattelrunner.exe", "daily, at logon", TaskStance.Telemetry),
            T(@"\Microsoft\Windows\Windows Error Reporting", "QueueReporting", true, "wermgr.exe -upload", "on an event", TaskStance.Telemetry),
            T(@"\", "GoogleUpdateTaskMachineUA", true, "C:\\Program Files (x86)\\Google\\Update\\GoogleUpdate.exe /ua /installsource scheduler", "daily", TaskStance.Optional),
            T(@"\", "MicrosoftEdgeUpdateTaskMachineCore", true, "C:\\Program Files (x86)\\Microsoft\\EdgeUpdate\\MicrosoftEdgeUpdate.exe /c", "at logon, daily", TaskStance.Optional),
            T(@"\", "NvTmRep_CrashReport3_...", true, "C:\\Program Files\\NVIDIA Corporation\\NvContainer\\nvtmrep.exe", "daily", TaskStance.Telemetry),
            T(@"\Microsoft\Windows\UpdateOrchestrator", "Schedule Scan", true, "usoclient.exe StartScan", "daily", TaskStance.KeepSystem),
            T(@"\Microsoft\Windows\Windows Defender", "Windows Defender Scheduled Scan", true, "MpCmdRun.exe Scan", "weekly", TaskStance.KeepSystem),
            T(@"\Microsoft\Windows\SystemRestore", "SR", true, "srtasks.exe ExecuteScheduledSPPCreation", "daily, at boot", TaskStance.KeepSystem),
            T(@"\Microsoft\Windows\Defrag", "ScheduledDefrag", true, "defrag.exe -c -h", "weekly", TaskStance.KeepSystem),
            T(@"\", "Adobe Acrobat Update Task", true, "AdobeARM.exe", "at logon, daily", TaskStance.Optional),
            T(@"\", "OneDrive Standalone Update Task-S-1-5-21", true, "OneDriveSetup.exe /update", "daily", TaskStance.Optional),
            T(@"\Custom", "BackupScript", true, "C:\\Scripts\\backup.cmd", "daily", TaskStance.Unreviewed),
        ];
    }

    private static string? StanceNote(TaskStance s) => s switch
    {
        TaskStance.KeepSystem => "Windows needs this. PowerX will not offer to disable it.",
        _ => null,
    };

    // ---- firewall ----------------------------------------------

    public static IReadOnlyList<FirewallRule> FirewallRules() =>
    [
        new() { Name = "Core Networking - DNS (UDP-Out)", Direction = FwDirection.Out, Action = FwAction.Allow, Enabled = true, Protocol = "UDP", RemotePorts = "53", Domain = true, Private = true, Public = true, Grouping = "Core Networking" },
        new() { Name = "Remote Desktop - User Mode (TCP-In)", Direction = FwDirection.In, Action = FwAction.Allow, Enabled = false, Protocol = "TCP", LocalPorts = "3389", Domain = true, Private = true, Public = true, Grouping = "Remote Desktop" },
        new() { Name = "Allow inbound 7777 (game server)", Direction = FwDirection.In, Action = FwAction.Allow, Enabled = true, Protocol = "TCP", LocalPorts = "7777", Domain = true, Private = true, Public = true },
        new() { Name = "Steam", Program = @"C:\Program Files (x86)\Steam\steam.exe", Direction = FwDirection.In, Action = FwAction.Allow, Enabled = true, Protocol = "any", Domain = true, Private = true, Public = true, Grouping = "Steam" },
        new() { Name = "Spotify", Program = @"C:\Users\user\AppData\Roaming\Spotify\Spotify.exe", Direction = FwDirection.Out, Action = FwAction.Allow, Enabled = true, Protocol = "any", Private = true },
        new() { Name = "Block old-app.exe outbound", Program = @"C:\Legacy\old-app.exe", Direction = FwDirection.Out, Action = FwAction.Block, Enabled = true, Protocol = "any", Domain = true, Private = true, Public = true },
        new() { Name = "File and Printer Sharing (SMB-In)", Direction = FwDirection.In, Action = FwAction.Allow, Enabled = true, Protocol = "TCP", LocalPorts = "445", Private = true, Grouping = "File and Printer Sharing" },
    ];

    // ---- event log --------------------------------------------

    public static IReadOnlyList<EventGroup> EventGroups()
    {
        var now = DateTimeOffset.Now;
        return
        [
            new() { Log = "System", Provider = "Microsoft-Windows-Kernel-Power", EventId = 41, Level = EventLevel2.Critical, Count = 1, FirstSeen = now.AddDays(-2), LastSeen = now.AddDays(-2), SampleMessage = "The system has rebooted without cleanly shutting down first.", Explanation = "The PC restarted without shutting down cleanly (power loss, a hard lock, or a hold of the power button). If it repeats, suspect the PSU, overheating, RAM, or a driver." },
            new() { Log = "System", Provider = "DCOM", EventId = 10016, Level = EventLevel2.Warning, Count = 34, FirstSeen = now.AddDays(-6), LastSeen = now.AddHours(-3), SampleMessage = "The application-specific permission settings do not grant Local Activation permission...", Explanation = "A DCOM permission warning. Almost always harmless and safe to ignore; Microsoft has said as much." },
            new() { Log = "Application", Provider = "Application Error", EventId = 1000, Level = EventLevel2.Error, Count = 3, FirstSeen = now.AddDays(-4), LastSeen = now.AddHours(-20), SampleMessage = "Faulting application name: game.exe, version 1.4.2.0", Explanation = "A desktop program crashed. The Crash insights page has the details." },
            new() { Log = "System", Provider = "Service Control Manager", EventId = 7009, Level = EventLevel2.Error, Count = 2, FirstSeen = now.AddDays(-3), LastSeen = now.AddDays(-1), SampleMessage = "A timeout was reached (30000 milliseconds) while waiting for the OVRService service to connect.", Explanation = "A service failed to start or timed out. If it names a driver or a feature you use, worth investigating; many are optional services that fail quietly." },
            new() { Log = "System", Provider = "Microsoft-Windows-DNS-Client", EventId = 1014, Level = EventLevel2.Warning, Count = 11, FirstSeen = now.AddDays(-5), LastSeen = now.AddHours(-8), SampleMessage = "Name resolution for the name cdn.example.net timed out.", Explanation = "A DNS name resolution timed out. Network or DNS server hiccup." },
        ];
    }

    // ---- storage explorer -----------------------------------------

    public static IReadOnlyList<FolderEntry> FolderEntries(string path)
    {
        string P(string name) => System.IO.Path.Combine(path, name);
        return
        [
            new(P("Windows"), "Windows", true, 32_400_000_000, 210_000),
            new(P("Program Files"), "Program Files", true, 24_800_000_000, 95_000),
            new(P("Users"), "Users", true, 61_300_000_000, 480_000),
            new(P("Program Files (x86)"), "Program Files (x86)", true, 9_100_000_000, 61_000),
            new(P("ProgramData"), "ProgramData", true, 6_700_000_000, 42_000),
            new(P("hiberfil.sys"), "hiberfil.sys", false, 13_600_000_000, 1),
            new(P("pagefile.sys"), "pagefile.sys", false, 9_800_000_000, 1),
            new(P("$Recycle.Bin"), "$Recycle.Bin", true, 2_100_000_000, 3_400),
        ];
    }
}
