using System.Runtime.InteropServices;

namespace PowerX.Core.Interop;

/// <summary>
/// Minimal PDH (Performance Data Helper) wrapper for wildcard multi-instance counters —
/// the documented way Task Manager reads GPU engine utilisation.
/// </summary>
internal static partial class Pdh
{
    internal const uint PDH_FMT_DOUBLE = 0x00000200;
    internal const uint PDH_FMT_NOCAP100 = 0x00008000;
    internal const int PDH_MORE_DATA = unchecked((int)0x800007D2);
    internal const int PDH_NO_DATA = unchecked((int)0x800007D5);
    internal const int PDH_INVALID_DATA = unchecked((int)0xC0000BC6);
    internal const int PDH_CALC_NEGATIVE_DENOMINATOR = unchecked((int)0x800007D6);
    internal const int PDH_CALC_NEGATIVE_VALUE = unchecked((int)0x800007D8);

    [LibraryImport("pdh.dll")]
    internal static partial int PdhOpenQueryW(nint dataSource, nuint userData, out nint query);

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int PdhAddEnglishCounterW(nint query, string counterPath, nuint userData, out nint counter);

    [LibraryImport("pdh.dll")]
    internal static partial int PdhCollectQueryData(nint query);

    [LibraryImport("pdh.dll")]
    internal static partial int PdhCloseQuery(nint query);

    [LibraryImport("pdh.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int PdhGetFormattedCounterArrayW(
        nint counter, uint format, ref uint bufferSize, out uint itemCount, nint buffer);

    // PDH_FMT_COUNTERVALUE_ITEM_W on x64: LPWSTR szName (8) + PDH_FMT_COUNTERVALUE { DWORD CStatus @0, double @8 } (16) = 24
    internal const int CounterValueItemSize = 24;
    internal const int ItemDoubleOffset = 16;

    /// <summary>One PDH sample cycle over a wildcard counter → (instanceName, value) pairs.</summary>
    internal static IReadOnlyList<(string Name, double Value)> ReadArray(nint counter)
    {
        uint size = 0, count = 0;
        int status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, 0);
        if (status != PDH_MORE_DATA || size == 0) return [];

        nint buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            status = PdhGetFormattedCounterArrayW(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, out count, buffer);
            if (status != 0) return [];

            var result = new List<(string, double)>((int)count);
            unsafe
            {
                byte* p = (byte*)buffer;
                for (int i = 0; i < count; i++)
                {
                    byte* item = p + i * CounterValueItemSize;
                    nint namePtr = *(nint*)item;
                    uint cstatus = *(uint*)(item + 8);
                    double value = *(double*)(item + ItemDoubleOffset);
                    string name = namePtr != 0 ? Marshal.PtrToStringUni(namePtr) ?? "" : "";
                    if (cstatus == 0) result.Add((name, value));
                }
            }
            return result;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}
