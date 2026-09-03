using System.Text;

namespace PowerX.Core.Diagnostics.Crash;

public sealed record DumpModule(string Name, ulong Base, uint Size, string? Version, uint TimeStamp)
{
    public bool Contains(ulong address) => address >= Base && address < Base + Size;
}

public sealed record MinidumpSummary
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public string? OsVersion { get; init; }
    public string? Architecture { get; init; }
    public uint? ExceptionCode { get; init; }
    public ulong? FaultAddress { get; init; }
    public IReadOnlyList<ulong> ExceptionParameters { get; init; } = [];
    public IReadOnlyList<DumpModule> Modules { get; init; } = [];
    /// <summary>The module whose address range contains the faulting instruction, if any.</summary>
    public DumpModule? FaultingModule { get; init; }

    public static MinidumpSummary Unreadable(string why) => new() { Ok = false, Error = why };
}

/// <summary>
/// Reads only the metadata streams of a user-mode minidump — system info, the module list, and
/// the exception record. It never touches the memory streams, never follows a pointer into
/// captured memory, and validates every offset against the file length. A malformed or hostile
/// dump yields <see cref="MinidumpSummary.Unreadable"/>, never a throw.
///
/// Symbols are NOT used and NOT downloaded: without them PowerX can name the faulting *module*
/// but not the function. That limitation is stated in the report, not hidden.
/// </summary>
public static class MinidumpReader
{
    private const uint Signature = 0x504D_444D;   // 'MDMP'
    private const int StreamModuleList = 4;
    private const int StreamException = 6;
    private const int StreamSystemInfo = 7;
    private const int MaxStreams = 128;
    private const int MaxModules = 4096;
    private const long MaxStreamBytes = 8 * 1024 * 1024;   // a module list / exception stream is tiny

    public static MinidumpSummary Read(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            long len = fs.Length;
            if (len < 32) return MinidumpSummary.Unreadable("file is too small to be a minidump");

            using var br = new BinaryReader(fs, Encoding.Unicode, leaveOpen: true);

            if (br.ReadUInt32() != Signature) return MinidumpSummary.Unreadable("not a minidump (bad signature)");
            uint version = br.ReadUInt32();
            if ((version & 0xFFFF) != 0xA793) return MinidumpSummary.Unreadable("unrecognised minidump version");
            uint streamCount = br.ReadUInt32();
            uint dirRva = br.ReadUInt32();
            if (streamCount == 0 || streamCount > MaxStreams) return MinidumpSummary.Unreadable("implausible stream count");
            if (!InFile(dirRva, streamCount * 12L, len)) return MinidumpSummary.Unreadable("stream directory out of range");

            // Locate the three streams we care about.
            (uint rva, uint size) modList = default, ex = default, sysInfo = default;
            fs.Position = dirRva;
            for (uint i = 0; i < streamCount; i++)
            {
                uint type = br.ReadUInt32();
                uint size = br.ReadUInt32();
                uint rva = br.ReadUInt32();
                if (size == 0 || size > MaxStreamBytes || !InFile(rva, size, len)) continue;
                switch (type)
                {
                    case StreamModuleList: modList = (rva, size); break;
                    case StreamException: ex = (rva, size); break;
                    case StreamSystemInfo: sysInfo = (rva, size); break;
                }
            }

            string? os = null, arch = null;
            if (sysInfo.size >= 24)
            {
                fs.Position = sysInfo.rva;
                ushort procArch = br.ReadUInt16();
                br.ReadUInt16(); br.ReadUInt16();          // level, revision
                br.ReadByte(); br.ReadByte();              // #procs, product type
                uint major = br.ReadUInt32(), minor = br.ReadUInt32(), build = br.ReadUInt32();
                os = $"Windows {major}.{minor} build {build}";
                arch = procArch switch { 0 => "x86", 9 => "x64", 12 => "ARM64", 5 => "ARM", _ => $"arch {procArch}" };
            }

            uint? exCode = null; ulong? faultAddr = null;
            var exParams = new List<ulong>();
            if (ex.size >= 8 + 40)
            {
                fs.Position = ex.rva + 8;                  // skip ThreadId + alignment
                exCode = br.ReadUInt32();
                br.ReadUInt32();                           // ExceptionFlags
                br.ReadUInt64();                           // ExceptionRecord (nested — ignored)
                faultAddr = br.ReadUInt64();
                uint nParams = br.ReadUInt32();
                br.ReadUInt32();                           // alignment
                nParams = Math.Min(nParams, 15);
                long room = (ex.rva + ex.size) - fs.Position;
                for (uint i = 0; i < nParams && room >= 8; i++, room -= 8) exParams.Add(br.ReadUInt64());
            }

            var modules = new List<DumpModule>();
            if (modList.size >= 4)
            {
                fs.Position = modList.rva;
                uint count = br.ReadUInt32();
                count = Math.Min(count, MaxModules);
                // MINIDUMP_MODULE is 108 bytes.
                if (InFile(modList.rva + 4, count * 108L, len))
                {
                    for (uint i = 0; i < count; i++)
                    {
                        long entry = modList.rva + 4 + i * 108L;
                        fs.Position = entry;
                        ulong baseOfImage = br.ReadUInt64();
                        uint sizeOfImage = br.ReadUInt32();
                        br.ReadUInt32();                         // CheckSum
                        uint timeStamp = br.ReadUInt32();
                        uint nameRva = br.ReadUInt32();
                        // VS_FIXEDFILEINFO: skip sig(4)+strucver(4), read fileVerMS/LS
                        fs.Position = entry + 24 + 8;
                        uint fileVerMs = br.ReadUInt32(), fileVerLs = br.ReadUInt32();
                        string? ver = (fileVerMs | fileVerLs) == 0
                            ? null
                            : $"{fileVerMs >> 16}.{fileVerMs & 0xFFFF}.{fileVerLs >> 16}.{fileVerLs & 0xFFFF}";

                        string name = ReadMinidumpString(fs, br, nameRva, len);
                        if (name.Length > 0)
                            modules.Add(new DumpModule(name, baseOfImage, sizeOfImage, ver, timeStamp));
                    }
                }
            }

            DumpModule? faulting = faultAddr is { } fa ? modules.FirstOrDefault(m => m.Contains(fa)) : null;

            return new MinidumpSummary
            {
                Ok = true,
                OsVersion = os,
                Architecture = arch,
                ExceptionCode = exCode,
                FaultAddress = faultAddr,
                ExceptionParameters = exParams,
                Modules = modules,
                FaultingModule = faulting,
            };
        }
        catch (Exception ex)
        {
            return MinidumpSummary.Unreadable(ex.Message);
        }
    }

    private static string ReadMinidumpString(FileStream fs, BinaryReader br, uint rva, long len)
    {
        if (!InFile(rva, 4, len)) return "";
        fs.Position = rva;
        uint byteLen = br.ReadUInt32();
        if (byteLen == 0 || byteLen > 64 * 1024 || !InFile(rva + 4, byteLen, len)) return "";
        var bytes = br.ReadBytes((int)byteLen);
        string full = Encoding.Unicode.GetString(bytes);
        int slash = full.LastIndexOfAny(['\\', '/']);
        return slash >= 0 ? full[(slash + 1)..] : full;
    }

    private static bool InFile(long rva, long size, long fileLen) =>
        rva >= 0 && size >= 0 && rva <= fileLen && rva + size <= fileLen;
}
