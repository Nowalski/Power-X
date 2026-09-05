using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PowerX.Core.Interop;

/// <summary>One physical (or software) display adapter as DXGI itself enumerates it — the same
/// list Task Manager's GPU picker is built from, and the source of truth for how many real GPUs
/// this machine has (WMI's <c>Win32_VideoController</c> alone is not: it does not expose the LUID
/// the GPU performance counters key their per-adapter data on, and its <c>AdapterRAM</c> field is
/// a signed 32-bit value that saturates at 4 GB on anything with more VRAM).</summary>
public sealed record DxgiAdapter(string Description, long Luid, ulong DedicatedVideoMemory,
    ulong DedicatedSystemMemory, ulong SharedSystemMemory, bool IsSoftwareOrRemote);

/// <summary>
/// Enumerates display adapters via <c>IDXGIFactory1::EnumAdapters1</c>. Read-only: it never
/// creates a device or renders anything, just asks DXGI what adapters exist. The interfaces below
/// are declared as flat vtables (every method from every base interface, in declared order) since
/// classic COM has no interface inheritance at the ABI level — placeholder names on methods this
/// class never calls exist only to keep their vtable slot correctly positioned for the ones it does.
/// </summary>
[SupportedOSPlatform("windows")]
public static class Dxgi
{
    private const uint DXGI_ERROR_NOT_FOUND = 0x887A0002;
    private const uint FLAG_REMOTE = 0x1;
    private const uint FLAG_SOFTWARE = 0x2;

    public static IReadOnlyList<DxgiAdapter> EnumerateAdapters()
    {
        var list = new List<DxgiAdapter>();
        try
        {
            Guid factoryIid = typeof(IDXGIFactory1).GUID;
            if (CreateDXGIFactory1(ref factoryIid, out nint pFactory) != 0 || pFactory == 0)
                return list;

            var factory = (IDXGIFactory1)Marshal.GetTypedObjectForIUnknown(pFactory, typeof(IDXGIFactory1));
            Marshal.Release(pFactory);
            try
            {
                for (uint i = 0; ; i++)
                {
                    int hr = factory.EnumAdapters1(i, out nint pAdapter);
                    if (hr == unchecked((int)DXGI_ERROR_NOT_FOUND) || pAdapter == 0) break;
                    if (hr != 0) break;

                    var adapter = (IDXGIAdapter1)Marshal.GetTypedObjectForIUnknown(pAdapter, typeof(IDXGIAdapter1));
                    Marshal.Release(pAdapter);
                    try
                    {
                        if (adapter.GetDesc1(out var d) != 0) continue;
                        long luid = ((long)d.LuidHigh << 32) | (uint)d.LuidLow;
                        bool softwareOrRemote = (d.Flags & (FLAG_SOFTWARE | FLAG_REMOTE)) != 0;
                        list.Add(new DxgiAdapter(d.Description, luid, d.DedicatedVideoMemory,
                            d.DedicatedSystemMemory, d.SharedSystemMemory, softwareOrRemote));
                    }
                    finally { Marshal.FinalReleaseComObject(adapter); }
                }
            }
            finally { Marshal.FinalReleaseComObject(factory); }
        }
        catch (Exception) { /* no DXGI 1.1, or nothing to enumerate */ }
        return list;
    }

    [DllImport("dxgi.dll", ExactSpelling = true)]
    private static extern int CreateDXGIFactory1(ref Guid riid, out nint ppFactory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DXGI_ADAPTER_DESC1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public int LuidLow;    // LUID.LowPart (DWORD) — signed storage only, reassembled unsigned above
        public int LuidHigh;   // LUID.HighPart (LONG)
        public uint Flags;
    }

    [ComImport, Guid("770aae78-f26f-4dba-a829-253c83d1b387"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIFactory1
    {
        // IDXGIObject (4) — never called, kept only to hold their vtable slots.
        void SetPrivateData_Unused();
        void SetPrivateDataInterface_Unused();
        void GetPrivateData_Unused();
        void GetParent_Unused();
        // IDXGIFactory (5)
        void EnumAdapters_Unused();
        void MakeWindowAssociation_Unused();
        void GetWindowAssociation_Unused();
        void CreateSwapChain_Unused();
        void CreateSoftwareAdapter_Unused();
        // IDXGIFactory1 (2)
        [PreserveSig] int EnumAdapters1(uint adapterIndex, out nint ppAdapter);
        [PreserveSig] int IsCurrent_Unused();
    }

    [ComImport, Guid("29038f61-3839-4626-91fd-086879011a05"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDXGIAdapter1
    {
        // IDXGIObject (4)
        void SetPrivateData_Unused();
        void SetPrivateDataInterface_Unused();
        void GetPrivateData_Unused();
        void GetParent_Unused();
        // IDXGIAdapter (3)
        void EnumOutputs_Unused();
        void GetDesc_Unused();
        void CheckInterfaceSupport_Unused();
        // IDXGIAdapter1 (1)
        [PreserveSig] int GetDesc1(out DXGI_ADAPTER_DESC1 desc);
    }
}
