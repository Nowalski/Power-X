using PowerX.Core.Diagnostics.Crash;
using PowerX.Core.Processes;
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
}
