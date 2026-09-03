using System.Runtime.InteropServices;
using PowerX.Core.Interop;

namespace PowerX.Core.Diagnostics;

public sealed record MemoryTestProgress(int Pass, int TotalPasses, string Phase, double Percent, double MegabytesPerSecond);

public sealed record MemoryTestError(long ByteOffset, ulong Expected, ulong Actual);

public sealed record MemoryTestResult(
    long BytesTested,
    int Passes,
    TimeSpan Elapsed,
    double AverageMBps,
    IReadOnlyList<MemoryTestError> Errors)
{
    public bool Passed => Errors.Count == 0;
}

/// <summary>
/// A non-destructive user-space RAM test: allocates a large buffer and runs pattern
/// write/verify and moving-inversion passes over it. It can only test free memory and runs
/// alongside the OS, so it is not a substitute for MemTest86 — but it reliably catches
/// unstable overclocks and failing modules under sustained access.
/// </summary>
public static class MemoryTest
{
    private const int ChunkBytes = 32 * 1024 * 1024;

    private static readonly ulong[] Patterns =
    [
        0x0000000000000000UL,
        0xFFFFFFFFFFFFFFFFUL,
        0xAAAAAAAAAAAAAAAAUL,
        0x5555555555555555UL,
        0xDEADBEEFCAFEBABEUL,
    ];

    /// <summary>Bytes it is safe to request right now (≈ 70% of available physical memory, capped at 16 GiB).</summary>
    public static long SafeMaxBytes()
    {
        long avail = AvailablePhysicalBytes();
        return avail <= 0 ? 512L * 1024 * 1024 : Math.Min((long)(avail * 0.70), 16L * 1024 * 1024 * 1024);
    }

    private static long AvailablePhysicalBytes()
    {
        var status = new Kernel32.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Kernel32.MEMORYSTATUSEX>() };
        return Kernel32.GlobalMemoryStatusEx(ref status) ? (long)status.ullAvailPhys : 0;
    }

    public static MemoryTestResult Run(long bytesToTest, int passes, IProgress<MemoryTestProgress>? progress, CancellationToken ct)
    {
        bytesToTest = Math.Clamp(bytesToTest, ChunkBytes, SafeMaxBytes());
        passes = Math.Clamp(passes, 1, 20);
        int chunkCount = (int)(bytesToTest / ChunkBytes);

        var errors = new List<MemoryTestError>();
        var chunks = new List<byte[]>(chunkCount);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        long moved = 0;                       // bytes read/written so far, for the MB/s figure
        long tested = (long)chunkCount * ChunkBytes;
        long totalWork = tested * passes * 4; // 4 sweeps per pass (write, verify, write inverse, verify)

        void Report(int pass, string phase)
        {
            double pct = totalWork == 0 ? 0 : 100.0 * moved / totalWork;
            double mbps = sw.Elapsed.TotalSeconds > 0 ? moved / 1024.0 / 1024.0 / sw.Elapsed.TotalSeconds : 0;
            progress?.Report(new(pass, passes, phase, pct, mbps));
        }

        void Sweep(ulong value, bool write, int pass, string phase)
        {
            for (int c = 0; c < chunks.Count; c++)
            {
                ct.ThrowIfCancellationRequested();
                Span<ulong> span = MemoryMarshal.Cast<byte, ulong>(chunks[c].AsSpan());
                if (write)
                {
                    span.Fill(value);
                }
                else
                {
                    for (int i = 0; i < span.Length; i++)
                        if (span[i] != value && errors.Count < 1000)
                            errors.Add(new MemoryTestError((long)c * ChunkBytes + (long)i * 8, value, span[i]));
                }
                moved += ChunkBytes;
                Report(pass, phase);
            }
        }

        try
        {
            for (int i = 0; i < chunkCount; i++)
            {
                ct.ThrowIfCancellationRequested();

                // Bail out if the machine's free memory dropped since we sized the test —
                // another process may have grabbed RAM. Keeps us from forcing the OS to page.
                if (i % 8 == 0 && AvailablePhysicalBytes() < 512L * 1024 * 1024)
                {
                    chunkCount = i;
                    break;
                }

                chunks.Add(GC.AllocateUninitializedArray<byte>(ChunkBytes));
                progress?.Report(new(0, passes, "Allocating memory", 100.0 * (i + 1) / Math.Max(1, chunkCount), 0));
            }

            tested = (long)chunks.Count * ChunkBytes;
            totalWork = tested * passes * 4;

            for (int pass = 1; pass <= passes; pass++)
            {
                ulong pattern = Patterns[(pass - 1) % Patterns.Length];
                Sweep(pattern, write: true, pass, $"Writing 0x{pattern:X16}");
                Sweep(pattern, write: false, pass, "Verifying");
                ulong inv = ~pattern;
                Sweep(inv, write: true, pass, "Writing inverse");
                Sweep(inv, write: false, pass, "Verifying inverse");
                if (errors.Count > 200) break;
            }
        }
        catch (OperationCanceledException) { }
        finally { chunks.Clear(); }

        sw.Stop();
        double avg = sw.Elapsed.TotalSeconds > 0 ? moved / 1024.0 / 1024.0 / sw.Elapsed.TotalSeconds : 0;
        return new MemoryTestResult(tested, passes, sw.Elapsed, avg, errors);
    }
}
