using System.Management;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Microsoft.Extensions.Logging.Abstractions;
using PowerX.Core.Interop;

namespace PowerX.Core.Telemetry;

/// <summary>
/// GPU utilisation via PDH wildcard counters (\GPU Engine, \GPU Adapter Memory) — the same
/// source Task Manager uses. Adapter descriptions come from WMI (queried once). Vendor
/// temperature/power/clocks are NOT exposed by any in-box API and are intentionally absent.
/// </summary>
public sealed class GpuMetricsProvider : IDisposable
{
    private readonly ILogger _log;
    private nint _query;
    private nint _engineCounter;
    private nint _dedicatedCounter;
    private nint _sharedCounter;
    private bool _primed;
    private bool _broken;

    public GpuMetricsProvider(ILogger<GpuMetricsProvider>? log = null)
    {
        _log = log ?? NullLogger<GpuMetricsProvider>.Instance;
        TryOpen();
    }

    private void TryOpen()
    {
        try
        {
            if (Pdh.PdhOpenQueryW(0, 0, out _query) != 0) { _broken = true; return; }
            Pdh.PdhAddEnglishCounterW(_query, @"\GPU Engine(*)\Utilization Percentage", 0, out _engineCounter);
            Pdh.PdhAddEnglishCounterW(_query, @"\GPU Adapter Memory(*)\Dedicated Usage", 0, out _dedicatedCounter);
            Pdh.PdhAddEnglishCounterW(_query, @"\GPU Adapter Memory(*)\Shared Usage", 0, out _sharedCounter);
            Pdh.PdhCollectQueryData(_query);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "PDH GPU query open failed");
            _broken = true;
        }
    }

    public ProviderResult<GpuMetrics> Sample()
    {
        if (_broken) return ProviderResult<GpuMetrics>.NotAvailable("GPU performance counters unavailable");
        try
        {
            if (Pdh.PdhCollectQueryData(_query) != 0)
                return ProviderResult<GpuMetrics>.NotAvailable("No GPU counter data");

            var engineItems = Pdh.ReadArray(_engineCounter);
            var byType = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, value) in engineItems)
            {
                string type = ParseEngineType(name);
                byType[type] = byType.GetValueOrDefault(type) + value;
            }

            var engines = byType
                .Select(kv => new GpuEngineLoad(kv.Key, Math.Clamp(kv.Value, 0, 100)))
                .OrderByDescending(e => e.Percent)
                .ToList();
            double overall = engines.Count > 0 ? engines[0].Percent : 0;

            ulong dedicated = (ulong)Pdh.ReadArray(_dedicatedCounter).Sum(x => x.Value);
            ulong shared = (ulong)Pdh.ReadArray(_sharedCounter).Sum(x => x.Value);

            var metrics = new GpuMetrics
            {
                UtilizationPercent = overall,
                Engines = engines,
                DedicatedMemoryUsed = dedicated,
                SharedMemoryUsed = shared,
                Timestamp = DateTimeOffset.UtcNow,
            };

            if (!_primed) { _primed = true; return ProviderResult<GpuMetrics>.Approximate(metrics, "priming"); }
            return ProviderResult<GpuMetrics>.Ok(metrics);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "GPU sample failed");
            return ProviderResult<GpuMetrics>.NotAvailable(ex.Message);
        }
    }

    private static string ParseEngineType(string instance)
    {
        int i = instance.IndexOf("engtype_", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return "Other";
        string raw = instance[(i + 8)..].Trim();
        return raw switch
        {
            "3D" => "3D",
            "VideoDecode" => "Video Decode",
            "VideoEncode" => "Video Encode",
            "VideoProcessing" => "Video Processing",
            "Copy" => "Copy",
            "Compute" => "Compute",
            "Security" => "Security",
            _ => raw,
        };
    }

    public static IReadOnlyList<GpuAdapter> QueryAdapters()
    {
        var list = new List<GpuAdapter>();
        try
        {
            using var s = new ManagementObjectSearcher(
                "SELECT Name, DriverVersion, AdapterRAM, VideoProcessor, CurrentHorizontalResolution, CurrentVerticalResolution, CurrentRefreshRate FROM Win32_VideoController");
            foreach (var o in s.Get())
            {
                string name = o["Name"]?.ToString()?.Trim() ?? "Display adapter";
                // Win32_VideoController.AdapterRAM is a signed 32-bit field — it saturates at 4 GB,
                // so a 16 GB card reads "4 GB". The driver's registry key carries the real size
                // as a 64-bit QWORD; prefer it whenever it is larger.
                ulong wmiRam = o["AdapterRAM"] is not null ? unchecked((ulong)Convert.ToInt64(o["AdapterRAM"])) : 0;
                ulong regRam = QwMemorySizeFor(name);
                list.Add(new GpuAdapter
                {
                    Name = name,
                    DriverVersion = o["DriverVersion"]?.ToString() ?? "",
                    DedicatedMemoryTotal = Math.Max(wmiRam, regRam),
                    VideoProcessor = o["VideoProcessor"]?.ToString() ?? "",
                    CurrentResolution = (ToInt(o["CurrentHorizontalResolution"]), ToInt(o["CurrentVerticalResolution"])),
                    RefreshHz = ToInt(o["CurrentRefreshRate"]),
                });
            }
        }
        catch (ManagementException) { /* WMI unavailable */ }
        return list;

        static int ToInt(object? v) => v is null ? 0 : Convert.ToInt32(v);
    }

    // HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-...}\<NNNN> holds one subkey per display
    // adapter; HardwareInformation.qwMemorySize is the dedicated VRAM in bytes (REG_QWORD or 8-byte
    // REG_BINARY). Match on the driver description so multi-GPU systems line up.
    private static ulong QwMemorySizeFor(string adapterName)
    {
        const string classKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(classKey);
            if (cls is null) return 0;
            foreach (var sub in cls.GetSubKeyNames())
            {
                if (sub.Length != 4 || !int.TryParse(sub, out _)) continue;
                using var k = cls.OpenSubKey(sub);
                string desc = (k?.GetValue("DriverDesc") as string ?? "").Trim();
                if (desc.Length == 0) continue;

                ulong bytes = k!.GetValue("HardwareInformation.qwMemorySize") switch
                {
                    long l => unchecked((ulong)l),
                    byte[] b when b.Length == 8 => BitConverter.ToUInt64(b, 0),
                    int i => (ulong)(uint)i,
                    _ => 0,
                };
                if (bytes == 0) continue;

                if (string.Equals(desc, adapterName, StringComparison.OrdinalIgnoreCase)
                    || adapterName.Contains(desc, StringComparison.OrdinalIgnoreCase)
                    || desc.Contains(adapterName, StringComparison.OrdinalIgnoreCase))
                    return bytes;
            }
        }
        catch (Exception) { /* registry layout varies; fall back to WMI value */ }
        return 0;
    }

    public void Dispose()
    {
        if (_query != 0) { Pdh.PdhCloseQuery(_query); _query = 0; }
    }
}
