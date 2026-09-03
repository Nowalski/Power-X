using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PowerX.Core.Interop;

namespace PowerX.Core.Telemetry;

/// <summary>
/// Overall CPU via <c>GetSystemTimes</c> deltas; per-logical-processor via
/// <c>NtQuerySystemInformation(SystemProcessorPerformanceInformation)</c> deltas.
/// Stateful: call <see cref="Sample"/> on a fixed cadence (default UI cadence: 1 s).
/// </summary>
public sealed class CpuMetricsProvider
{
    private readonly ILogger _log;
    private readonly int _logicalCount;
    private long _prevIdle, _prevKernel, _prevUser;
    private NtDll.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[] _prevPerCpu;
    private bool _primed;

    public CpuMetricsProvider(ILogger<CpuMetricsProvider>? log = null)
    {
        _log = log ?? NullLogger<CpuMetricsProvider>.Instance;
        _logicalCount = Environment.ProcessorCount;
        _prevPerCpu = new NtDll.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[_logicalCount];
    }

    public ProviderResult<CpuMetrics> Sample()
    {
        try
        {
            if (!Kernel32.GetSystemTimes(out long idle, out long kernel, out long user))
            {
                return ProviderResult<CpuMetrics>.NotAvailable("GetSystemTimes failed");
            }

            var perCpu = QueryPerCpu();
            var perCpuUsage = new double[_logicalCount];

            double total = 0, kernelShare = 0;
            if (_primed)
            {
                long dIdle = idle - _prevIdle;
                long dKernel = kernel - _prevKernel;
                long dUser = user - _prevUser;
                long dBusy = dKernel + dUser - dIdle; // kernel time includes idle
                long dTotal = dKernel + dUser;
                if (dTotal > 0)
                {
                    total = Math.Clamp(100.0 * dBusy / dTotal, 0, 100);
                    kernelShare = Math.Clamp(100.0 * (dKernel - dIdle) / dTotal, 0, 100);
                }

                if (perCpu is not null)
                {
                    for (int i = 0; i < _logicalCount && i < perCpu.Length; i++)
                    {
                        long cIdle = perCpu[i].IdleTime - _prevPerCpu[i].IdleTime;
                        long cKernel = perCpu[i].KernelTime - _prevPerCpu[i].KernelTime;
                        long cUser = perCpu[i].UserTime - _prevPerCpu[i].UserTime;
                        long cTotal = cKernel + cUser;
                        perCpuUsage[i] = cTotal > 0
                            ? Math.Clamp(100.0 * (cTotal - cIdle) / cTotal, 0, 100)
                            : 0;
                    }
                }
            }

            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;
            if (perCpu is not null) _prevPerCpu = perCpu;
            _primed = true;

            Kernel32.PERFORMANCE_INFORMATION pi = default;
            bool havePi = Kernel32.GetPerformanceInfo(ref pi, (uint)Marshal.SizeOf<Kernel32.PERFORMANCE_INFORMATION>());

            var metrics = new CpuMetrics
            {
                TotalUsagePercent = total,
                KernelUsagePercent = kernelShare,
                PerLogicalProcessor = perCpuUsage,
                ProcessCount = havePi ? (int)pi.ProcessCount : 0,
                ThreadCount = havePi ? (int)pi.ThreadCount : 0,
                HandleCount = havePi ? (int)pi.HandleCount : 0,
                Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
                Timestamp = DateTimeOffset.UtcNow,
            };

            if (!_primed) return ProviderResult<CpuMetrics>.Approximate(metrics, "priming");
            return perCpu is null
                ? ProviderResult<CpuMetrics>.Approximate(metrics, "per-core data unavailable")
                : ProviderResult<CpuMetrics>.Ok(metrics);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "CPU sample failed");
            return ProviderResult<CpuMetrics>.NotAvailable(ex.Message);
        }
    }

    private unsafe NtDll.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[]? QueryPerCpu()
    {
        int size = Marshal.SizeOf<NtDll.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>() * _logicalCount;
        nint buffer = Marshal.AllocHGlobal(size);
        try
        {
            int status = NtDll.NtQuerySystemInformation(
                NtDll.SystemProcessorPerformanceInformation, buffer, (uint)size, out _);
            if (status != 0) return null;

            var result = new NtDll.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[_logicalCount];
            var span = new ReadOnlySpan<NtDll.SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>((void*)buffer, _logicalCount);
            span.CopyTo(result);
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
