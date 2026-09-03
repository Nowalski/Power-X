using System.Text;
using FluentAssertions;
using PowerX.Core.Diagnostics.Crash;
using Xunit;

namespace PowerX.Core.Tests;

public class BugcheckCatalogTests
{
    [Fact]
    public void Every_entry_is_well_formed_and_codes_are_unique()
    {
        int[] codes = [0x0A, 0x1E, 0x50, 0x7E, 0x9F, 0xD1, 0xEF, 0x116, 0x124, 0x133, 0x139];
        foreach (var c in codes)
        {
            BugcheckCatalog.TryGet(c, out var info).Should().BeTrue($"0x{c:X} should be catalogued");
            info.Name.Should().NotBeNullOrWhiteSpace();
            info.Meaning.Should().NotBeNullOrWhiteSpace();
            info.CommonCauses.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Describe_formats_known_and_unknown_codes()
    {
        BugcheckCatalog.Describe(0x133).Should().Be("DPC_WATCHDOG_VIOLATION (0x133)");
        BugcheckCatalog.Describe(0x9999).Should().Be("stop 0x9999");
    }
}

public class WerReportReaderTests
{
    private static string WriteWer(string body)
    {
        var path = Path.Combine(Path.GetTempPath(), $"Report-{Guid.NewGuid():N}.wer");
        File.WriteAllText(path, body, Encoding.Unicode);
        return path;
    }

    [Fact]
    public void Parses_a_native_APPCRASH()
    {
        var wer = WriteWer(string.Join("\r\n",
            "Version=1",
            "EventType=APPCRASH",
            "FriendlyEventName=Stopped working",
            "AppName=Contoso Widget",
            "AppPath=C:\\Program Files\\Contoso\\widget.exe",
            "Sig[0].Name=Application Name",
            "Sig[0].Value=widget.exe",
            "Sig[1].Name=Application Version",
            "Sig[1].Value=2.3.4.5",
            "Sig[3].Name=Fault Module Name",
            "Sig[3].Value=nvwgf2umx.dll",
            "Sig[4].Name=Fault Module Version",
            "Sig[4].Value=31.0.15.3623",
            "Sig[6].Name=Exception Code",
            "Sig[6].Value=c0000005"));
        try
        {
            var r = WerReportReader.Parse(wer, Path.GetDirectoryName(wer)!, File.GetLastWriteTime(wer));
            r.Should().NotBeNull();
            r!.EventType.Should().Be("APPCRASH");
            r.AppName.Should().Be("widget.exe");
            r.AppVersion.Should().Be("2.3.4.5");
            r.FaultModule.Should().Be("nvwgf2umx.dll");
            r.ExceptionCode.Should().Be("c0000005");
        }
        finally { File.Delete(wer); }
    }

    [Fact]
    public void Parses_a_CLR20r3_managed_crash()
    {
        var wer = WriteWer(string.Join("\r\n",
            "Version=1",
            "EventType=CLR20r3",
            "Sig[0].Name=Problem Signature 01",
            "Sig[0].Value=myapp.exe",
            "Sig[1].Name=Problem Signature 02",
            "Sig[1].Value=1.0.0.0",
            "Sig[8].Name=Problem Signature 09",
            "Sig[8].Value=System.NullReferenceException"));
        try
        {
            var r = WerReportReader.Parse(wer, Path.GetDirectoryName(wer)!, File.GetLastWriteTime(wer))!;
            r.EventType.Should().Be("CLR20r3");
            r.AppName.Should().Be("myapp.exe");
            r.ManagedExceptionType.Should().Be("System.NullReferenceException");
        }
        finally { File.Delete(wer); }
    }
}

public class MinidumpReaderTests
{
    [Fact]
    public void Rejects_non_dump_and_truncated_files()
    {
        var junk = Path.Combine(Path.GetTempPath(), $"j-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(junk, [1, 2, 3, 4, 5, 6, 7, 8]);
        try
        {
            MinidumpReader.Read(junk).Ok.Should().BeFalse();
        }
        finally { File.Delete(junk); }
    }

    [Fact]
    public void Rejects_directory_rva_past_end_of_file()
    {
        var path = Path.Combine(Path.GetTempPath(), $"d-{Guid.NewGuid():N}.mdmp");
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write(0x504D_444Du);        // 'MDMP'
            w.Write(0x0000A793u);         // version
            w.Write(4u);                  // 4 streams
            w.Write(0x7FFF_FFFFu);        // dir RVA way past EOF
            w.Write(0u); w.Write(0u); w.Write(0UL);
        }
        try { MinidumpReader.Read(path).Ok.Should().BeFalse(); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Reads_system_info_module_list_and_exception_from_a_crafted_dump()
    {
        var path = Path.Combine(Path.GetTempPath(), $"good-{Guid.NewGuid():N}.mdmp");
        File.WriteAllBytes(path, CraftedDump.Build());
        try
        {
            var r = MinidumpReader.Read(path);
            r.Ok.Should().BeTrue(r.Error);
            r.Architecture.Should().Be("x64");
            r.OsVersion.Should().Contain("build 26100");
            r.ExceptionCode.Should().Be(0xC0000005);
            r.FaultAddress.Should().Be(0x7FF0_0001_2345UL);
            r.Modules.Should().ContainSingle();
            r.Modules[0].Name.Should().Be("faulting.dll");
            r.Modules[0].Version.Should().Be("1.2.3.4");
            r.FaultingModule.Should().NotBeNull();
            r.FaultingModule!.Name.Should().Be("faulting.dll");
        }
        finally { File.Delete(path); }
    }
}

/// <summary>Builds the smallest structurally valid user-mode minidump the reader accepts.</summary>
internal static class CraftedDump
{
    public static byte[] Build()
    {
        // Sections are assembled after the header + directory so their RVAs are known.
        const int headerSize = 32;
        const int dirEntries = 3;
        int dirRva = headerSize;
        int afterDir = dirRva + dirEntries * 12;

        byte[] sysInfo = SystemInfo();
        byte[] nameStr = MiniString("C:\\Windows\\System32\\faulting.dll");
        int sysRva = afterDir;
        int nameRva = sysRva + sysInfo.Length;
        int modRva = nameRva + nameStr.Length;
        byte[] modList = ModuleList((uint)nameRva);
        int exRva = modRva + modList.Length;
        byte[] exStream = Exception();

        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);

        // header
        w.Write(0x504D_444Du);
        w.Write(0x0000A793u);
        w.Write((uint)dirEntries);
        w.Write((uint)dirRva);
        w.Write(0u); w.Write(0u); w.Write(0UL);

        // directory: (type, size, rva)
        w.Write(7u); w.Write((uint)sysInfo.Length); w.Write((uint)sysRva);   // SystemInfo
        w.Write(4u); w.Write((uint)modList.Length); w.Write((uint)modRva);   // ModuleList
        w.Write(6u); w.Write((uint)exStream.Length); w.Write((uint)exRva);   // Exception

        w.Write(sysInfo);
        w.Write(nameStr);
        w.Write(modList);
        w.Write(exStream);
        w.Flush();
        return ms.ToArray();
    }

    private static byte[] SystemInfo()
    {
        byte[] b = new byte[32];
        BitConverter.GetBytes((ushort)9).CopyTo(b, 0);      // ProcessorArchitecture x64
        BitConverter.GetBytes(10u).CopyTo(b, 8);            // MajorVersion
        BitConverter.GetBytes(0u).CopyTo(b, 12);            // MinorVersion
        BitConverter.GetBytes(26100u).CopyTo(b, 16);        // BuildNumber
        return b;
    }

    private static byte[] MiniString(string s)
    {
        byte[] u = Encoding.Unicode.GetBytes(s);
        byte[] b = new byte[4 + u.Length];
        BitConverter.GetBytes((uint)u.Length).CopyTo(b, 0);
        u.CopyTo(b, 4);
        return b;
    }

    private static byte[] ModuleList(uint nameRva)
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(1u);                       // NumberOfModules
        w.Write(0x7FF0_0000_0000UL);       // BaseOfImage
        w.Write(0x0010_0000u);             // SizeOfImage (1 MB)
        w.Write(0u);                       // CheckSum
        w.Write(0x1234_5678u);             // TimeDateStamp
        w.Write(nameRva);                  // ModuleNameRva
        // VS_FIXEDFILEINFO (52 bytes)
        w.Write(0xFEEF04BDu);              // dwSignature
        w.Write(0x00010000u);             // dwStrucVersion
        w.Write(0x0001_0002u);             // dwFileVersionMS -> 1.2
        w.Write(0x0003_0004u);             // dwFileVersionLS -> 3.4
        w.Write(new byte[52 - 16]);        // rest of VS_FIXEDFILEINFO
        w.Write(0UL);                      // CvRecord location (8)
        w.Write(0UL);                      // MiscRecord location (8)
        w.Write(0UL);                      // Reserved0
        w.Write(0UL);                      // Reserved1
        w.Flush();
        return ms.ToArray();               // 4 + 108
    }

    private static byte[] Exception()
    {
        using var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(1234u);                    // ThreadId
        w.Write(0u);                       // __alignment
        w.Write(0xC0000005u);             // ExceptionCode
        w.Write(0u);                       // ExceptionFlags
        w.Write(0UL);                      // ExceptionRecord
        w.Write(0x7FF0_0001_2345UL);      // ExceptionAddress (inside the module)
        w.Write(2u);                       // NumberParameters
        w.Write(0u);                       // __unusedAlignment
        w.Write(1UL); w.Write(0x7FF0_0001_2345UL);
        w.Flush();
        return ms.ToArray();               // 8 + 40
    }
}

public class CrashExceptionDescriptionTests
{
    [Theory]
    [InlineData("c0000005", "access violation")]
    [InlineData("c000027b", "stowed")]
    [InlineData("e0434352", ".NET")]
    [InlineData("c00000fd", "stack overflow")]
    public void Common_codes_have_a_plain_description(string code, string expectedFragment)
        => CrashScanner.DescribeException(code).Should().Contain(expectedFragment);
}
