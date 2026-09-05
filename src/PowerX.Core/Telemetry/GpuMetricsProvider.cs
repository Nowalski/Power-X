using System.Management;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Microsoft.Extensions.Logging.Abstractions;
using PowerX.Core.Interop;

namespace PowerX.Core.Telemetry;

/// <summary>
/// GPU utilisation via PDH wildcard counters (\GPU Engine, \GPU Adapter Memory) — the same
/// source Task Manager uses. Adapter identity (name, LUID, real VRAM) comes from DXGI, which is
/// also what those counters key their per-adapter instance names on, so a machine with more than
/// one GPU gets a real breakdown instead of one blended number. Vendor temperature/power/clocks
/// are NOT exposed by any in-box API and are intentionally absent.
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
    private static IReadOnlyList<GpuAdapter>? _adapters;

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

            // Group engine utilisation by (adapter LUID, engine type) — the LUID is embedded in
            // every instance name (…luid_0xHHHHHHHH_0xLLLLLLLL…) and is the only way to tell two
            // GPUs' numbers apart; summing across everything the way an earlier version did makes
            // a second GPU's usage silently blend into the first one's.
            var byAdapterAndType = new Dictionary<long, Dictionary<string, double>>();
            foreach (var (name, value) in Pdh.ReadArray(_engineCounter))
            {
                long luid = ParseLuid(name);
                string type = ParseEngineType(name);
                var byType = byAdapterAndType.TryGetValue(luid, out var d) ? d : byAdapterAndType[luid] = new(StringComparer.OrdinalIgnoreCase);
                byType[type] = byType.GetValueOrDefault(type) + value;
            }

            var dedicatedByAdapter = SumByLuid(Pdh.ReadArray(_dedicatedCounter));
            var sharedByAdapter = SumByLuid(Pdh.ReadArray(_sharedCounter));

            var adapters = QueryAdapters();
            var perAdapter = new List<GpuAdapterUsage>();
            foreach (var a in adapters.Where(a => a.Luid != 0))
            {
                var byType = byAdapterAndType.GetValueOrDefault(a.Luid) ?? new Dictionary<string, double>();
                var engines = byType
                    .Select(kv => new GpuEngineLoad(kv.Key, Math.Clamp(kv.Value, 0, 100)))
                    .OrderByDescending(e => e.Percent)
                    .ToList();
                perAdapter.Add(new GpuAdapterUsage
                {
                    Luid = a.Luid,
                    Name = a.Name,
                    UtilizationPercent = engines.Count > 0 ? engines[0].Percent : 0,
                    Engines = engines,
                    DedicatedMemoryUsed = dedicatedByAdapter.GetValueOrDefault(a.Luid),
                    DedicatedMemoryTotal = a.DedicatedMemoryTotal,
                    SharedMemoryUsed = sharedByAdapter.GetValueOrDefault(a.Luid),
                });
            }

            // Blended totals across every adapter — what the Home tile and the GPU page's hero
            // gauge show, matching this method's behaviour before per-adapter data existed.
            var blendedByType = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var byType in byAdapterAndType.Values)
                foreach (var (type, value) in byType)
                    blendedByType[type] = blendedByType.GetValueOrDefault(type) + value;
            var blendedEngines = blendedByType
                .Select(kv => new GpuEngineLoad(kv.Key, Math.Clamp(kv.Value, 0, 100)))
                .OrderByDescending(e => e.Percent)
                .ToList();

            var metrics = new GpuMetrics
            {
                UtilizationPercent = blendedEngines.Count > 0 ? blendedEngines[0].Percent : 0,
                Engines = blendedEngines,
                DedicatedMemoryUsed = dedicatedByAdapter.Values.Aggregate(0UL, (a, b) => a + b),
                SharedMemoryUsed = sharedByAdapter.Values.Aggregate(0UL, (a, b) => a + b),
                Timestamp = DateTimeOffset.UtcNow,
                Adapters = perAdapter,
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

    private static Dictionary<long, ulong> SumByLuid(IEnumerable<(string Name, double Value)> items)
    {
        var result = new Dictionary<long, ulong>();
        foreach (var (name, value) in items)
        {
            long luid = ParseLuid(name);
            result[luid] = result.GetValueOrDefault(luid) + (ulong)Math.Max(0, value);
        }
        return result;
    }

    /// <summary>Extracts the adapter LUID from a PDH GPU-counter instance name, e.g.
    /// <c>pid_1234_luid_0x00000000_0x0001E92C_phys_0_eng_0_engtype_3D</c> or
    /// <c>luid_0x00000000_0x0001E92C_phys_0</c>. 0 (never a real LUID) if the segment is missing
    /// or malformed, so a garbled instance name groups harmlessly instead of throwing.</summary>
    internal static long ParseLuid(string instanceName)
    {
        int i = instanceName.IndexOf("luid_0x", StringComparison.OrdinalIgnoreCase);
        if (i < 0) return 0;
        var rest = instanceName.AsSpan(i + "luid_0x".Length);
        int sep = rest.IndexOf('_');
        if (sep < 0) return 0;
        if (!uint.TryParse(rest[..sep], System.Globalization.NumberStyles.HexNumber, null, out uint high)) return 0;
        var afterSep = rest[(sep + 1)..];
        if (afterSep.Length < 2 || afterSep[0] != '0' || (afterSep[1] != 'x' && afterSep[1] != 'X')) return 0;
        afterSep = afterSep[2..];
        int end = afterSep.IndexOf('_');
        var lowSpan = end < 0 ? afterSep : afterSep[..end];
        if (!uint.TryParse(lowSpan, System.Globalization.NumberStyles.HexNumber, null, out uint low)) return 0;
        return ((long)high << 32) | low;
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

    /// <summary>Every real display adapter, cheapest call first (DXGI, ~1 ms) merged with WMI's
    /// driver version and current display mode where a name match is found. Cached after the
    /// first call — adapters do not change without a restart PowerX would also need to survive.</summary>
    public static IReadOnlyList<GpuAdapter> QueryAdapters() => _adapters ??= BuildAdapterList();

    private static IReadOnlyList<GpuAdapter> BuildAdapterList()
    {
        var dxgi = Dxgi.EnumerateAdapters().Where(a => !a.IsSoftwareOrRemote).ToList();
        // A virtual/indirect-display driver (VR compositor, remote-desktop GPU passthrough) can
        // enumerate the same physical card again under a second LUID with Flags=0 — collapse
        // exact-name duplicates to the one DXGI reports the most VRAM for, which is consistently
        // the real entry in testing.
        var deduped = dxgi
            .GroupBy(a => a.Description, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderByDescending(a => a.DedicatedVideoMemory).First())
            .ToList();

        var wmi = QueryWmiAdapters();

        var list = new List<GpuAdapter>();
        foreach (var d in deduped)
        {
            var match = wmi.FirstOrDefault(w =>
                string.Equals(w.Name, d.Description, StringComparison.OrdinalIgnoreCase)
                || w.Name.Contains(d.Description, StringComparison.OrdinalIgnoreCase)
                || d.Description.Contains(w.Name, StringComparison.OrdinalIgnoreCase));
            list.Add(new GpuAdapter
            {
                Name = d.Description,
                Luid = d.Luid,
                DedicatedMemoryTotal = d.DedicatedVideoMemory,
                DriverVersion = match?.DriverVersion ?? "",
                VideoProcessor = match?.VideoProcessor ?? "",
                CurrentResolution = match?.CurrentResolution ?? default,
                RefreshHz = match?.RefreshHz ?? 0,
            });
        }

        // DXGI found nothing (very old GPU/driver, or DXGI 1.1 unavailable) — fall back to the
        // WMI-only view so the page still shows something rather than an empty adapter list.
        if (list.Count == 0)
            list.AddRange(wmi.Select(w => new GpuAdapter
            {
                Name = w.Name, Luid = 0, DedicatedMemoryTotal = w.DedicatedMemoryTotal,
                DriverVersion = w.DriverVersion, VideoProcessor = w.VideoProcessor,
                CurrentResolution = w.CurrentResolution, RefreshHz = w.RefreshHz,
            }));

        return list;
    }

    private static IReadOnlyList<GpuAdapter> QueryWmiAdapters()
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
                // so a 16 GB card reads "4 GB". DXGI (above) gives the real value; this is only
                // used as a last-resort fallback or for the registry-free metadata fields.
                ulong wmiRam = o["AdapterRAM"] is not null ? unchecked((ulong)Convert.ToInt64(o["AdapterRAM"])) : 0;
                list.Add(new GpuAdapter
                {
                    Name = name,
                    DriverVersion = o["DriverVersion"]?.ToString() ?? "",
                    DedicatedMemoryTotal = wmiRam,
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

    public void Dispose()
    {
        if (_query != 0) { Pdh.PdhCloseQuery(_query); _query = 0; }
    }
}
