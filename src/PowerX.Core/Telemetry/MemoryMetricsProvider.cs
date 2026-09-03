using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PowerX.Core.Interop;

namespace PowerX.Core.Telemetry;

/// <summary>
/// Physical memory via <c>GlobalMemoryStatusEx</c>; commit and pool figures via
/// <c>GetPerformanceInfo</c>. Stateless — safe to call at any cadence.
/// </summary>
public sealed class MemoryMetricsProvider
{
    private readonly ILogger _log;

    public MemoryMetricsProvider(ILogger<MemoryMetricsProvider>? log = null)
        => _log = log ?? NullLogger<MemoryMetricsProvider>.Instance;

    public ProviderResult<MemoryMetrics> Sample()
    {
        try
        {
            var status = new Kernel32.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Kernel32.MEMORYSTATUSEX>() };
            if (!Kernel32.GlobalMemoryStatusEx(ref status))
            {
                return ProviderResult<MemoryMetrics>.NotAvailable("GlobalMemoryStatusEx failed");
            }

            Kernel32.PERFORMANCE_INFORMATION pi = default;
            bool havePi = Kernel32.GetPerformanceInfo(ref pi, (uint)Marshal.SizeOf<Kernel32.PERFORMANCE_INFORMATION>());
            ulong pageSize = havePi ? (ulong)pi.PageSize : 4096;

            var metrics = new MemoryMetrics
            {
                TotalPhysical = status.ullTotalPhys,
                AvailablePhysical = status.ullAvailPhys,
                UsedPercent = status.dwMemoryLoad,
                CachedApprox = havePi ? (ulong)pi.SystemCache * pageSize : 0,
                CommitTotal = havePi ? (ulong)pi.CommitTotal * pageSize : 0,
                CommitLimit = havePi ? (ulong)pi.CommitLimit * pageSize : status.ullTotalPageFile,
                PagedPool = havePi ? (ulong)pi.KernelPaged * pageSize : 0,
                NonPagedPool = havePi ? (ulong)pi.KernelNonpaged * pageSize : 0,
                Timestamp = DateTimeOffset.UtcNow,
            };

            return havePi
                ? ProviderResult<MemoryMetrics>.Ok(metrics)
                : ProviderResult<MemoryMetrics>.Approximate(metrics, "pool/commit detail unavailable");
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Memory sample failed");
            return ProviderResult<MemoryMetrics>.NotAvailable(ex.Message);
        }
    }
}
