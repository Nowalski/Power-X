using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using PowerX.Core.Diagnostics;
using PowerX.Core.Processes;
using PowerX.Core.Telemetry;

BenchmarkRunner.Run<TelemetryBenchmarks>();

[MemoryDiagnoser]
public class TelemetryBenchmarks
{
    private readonly ProcessProvider _proc = new();
    private readonly CpuMetricsProvider _cpu = new();
    private readonly MemoryMetricsProvider _mem = new();
    private readonly GpuMetricsProvider _gpu = new();
    private ProcessSnapshot _snapshot = null!;

    [GlobalSetup]
    public void Setup()
    {
        _cpu.Sample();
        _proc.Enumerate();
        _gpu.Sample();
        _snapshot = _proc.Enumerate();
    }

    [Benchmark] public ProcessSnapshot EnumerateProcesses() => _proc.Enumerate();

    [Benchmark] public object SampleCpu() => _cpu.Sample();

    [Benchmark] public object SampleMemory() => _mem.Sample();

    [Benchmark] public object SampleGpu() => _gpu.Sample();

    [Benchmark] public IReadOnlyDictionary<int, List<ProcessInfo>> BuildTree() => ProcessProvider.BuildTree(_snapshot);

    [Benchmark] public object CpuInfo() => PowerX.Core.Diagnostics.CpuInfo.Query();
}
