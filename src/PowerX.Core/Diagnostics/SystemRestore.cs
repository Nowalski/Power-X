using System.Diagnostics;
using System.Globalization;
using System.Management;
using Microsoft.Win32;
using PowerX.Core.Processes;

namespace PowerX.Core.Diagnostics;

public sealed record RestorePoint(int SequenceNumber, string Description, DateTimeOffset Created, string Type);

/// <summary>
/// System Protection / System Restore via the documented <c>SystemRestore</c> WMI class.
/// PowerX can create a point (e.g. before a batch of tweaks) and list existing ones;
/// the actual restore is done through Windows' own <c>rstrui.exe</c> (it needs a reboot).
/// </summary>
public static class SystemRestore
{
    private const string Config = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore";

    public static bool IsEnabled()
    {
        using var key = Registry.LocalMachine.OpenSubKey(Config);
        // DisableSR / RPSessionInterval: 0/absent when disabled, non-zero interval when on.
        if (key?.GetValue("DisableSR") is 1) return false;
        return key?.GetValue("RPSessionInterval") is int i && i > 0
               || key?.GetValue("RPGlobalInterval") is not null;
    }

    public static ActionResult EnableForSystemDrive()
    {
        try
        {
            var scope = Connect();
            using var cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
            var inp = cls.GetMethodParameters("Enable");
            inp["Drive"] = @"C:\";
            var outp = cls.InvokeMethod("Enable", inp, null);
            return ToResult(outp, "Enable System Protection");
        }
        catch (ManagementException ex) { return ActionResult.Fail(ex.Message); }
        catch (UnauthorizedAccessException) { return ActionResult.Fail("Administrator rights required."); }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
    }

    /// <summary>
    /// Create a restore point. Windows throttles restore-point creation to once per 24h
    /// (<c>SystemRestorePointCreationFrequency</c>); this lifts the throttle for the one call
    /// and then puts the user's setting back exactly as it was.
    /// </summary>
    public static ActionResult Create(string description)
    {
        object? priorFrequency = null;
        try
        {
            using (var cfg = Registry.LocalMachine.CreateSubKey(Config, writable: true))
            {
                priorFrequency = cfg.GetValue("SystemRestorePointCreationFrequency");
                cfg.SetValue("SystemRestorePointCreationFrequency", 0, RegistryValueKind.DWord);
            }

            var scope = Connect();
            using var cls = new ManagementClass(scope, new ManagementPath("SystemRestore"), null);
            var inp = cls.GetMethodParameters("CreateRestorePoint");
            inp["Description"] = description;
            inp["RestorePointType"] = 12;  // MODIFY_SETTINGS
            inp["EventType"] = 100;         // BEGIN_SYSTEM_CHANGE
            var outp = cls.InvokeMethod("CreateRestorePoint", inp, null);
            return ToResult(outp, "Create restore point");
        }
        catch (ManagementException ex) when (ex.Message.Contains("not found", StringComparison.OrdinalIgnoreCase))
        {
            return ActionResult.Fail("System Protection is turned off for this PC.");
        }
        catch (ManagementException ex) { return ActionResult.Fail(ex.Message); }
        catch (UnauthorizedAccessException) { return ActionResult.Fail("Administrator rights required."); }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
        finally
        {
            try
            {
                using var cfg = Registry.LocalMachine.OpenSubKey(Config, writable: true);
                if (cfg is not null)
                {
                    if (priorFrequency is int p)
                        cfg.SetValue("SystemRestorePointCreationFrequency", p, RegistryValueKind.DWord);
                    else
                        cfg.DeleteValue("SystemRestorePointCreationFrequency", throwOnMissingValue: false);
                }
            }
            catch (Exception) { /* best effort — leaving the throttle off is the only downside */ }
        }
    }

    public static IReadOnlyList<RestorePoint> List()
    {
        var list = new List<RestorePoint>();
        try
        {
            var scope = Connect();
            using var s = new ManagementObjectSearcher(scope, new ObjectQuery("SELECT * FROM SystemRestore"));
            foreach (var o in s.Get())
            {
                list.Add(new RestorePoint(
                    Convert.ToInt32(o["SequenceNumber"]),
                    o["Description"]?.ToString() ?? "",
                    ParseWmiDate(o["CreationTime"]?.ToString()),
                    TypeName(Convert.ToInt32(o["RestorePointType"] ?? 0))));
            }
        }
        catch (Exception) { /* SR unavailable */ }
        return list.OrderByDescending(r => r.Created).ToList();
    }

    public static ActionResult OpenRestoreUi()
    {
        try
        {
            Process.Start(new ProcessStartInfo("rstrui.exe") { UseShellExecute = true });
            return ActionResult.Ok;
        }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
    }

    private static ManagementScope Connect()
    {
        var scope = new ManagementScope(@"\\.\root\default");
        scope.Connect();
        return scope;
    }

    private static ActionResult ToResult(ManagementBaseObject? outp, string what)
    {
        uint rv = outp?["ReturnValue"] is not null ? Convert.ToUInt32(outp["ReturnValue"]) : 1;
        return rv switch
        {
            0 => ActionResult.Ok,
            1058 => ActionResult.Fail("System Restore is disabled by policy."),
            _ => ActionResult.Fail($"{what} failed (code {rv})."),
        };
    }

    private static DateTimeOffset ParseWmiDate(string? wmi)
    {
        // yyyyMMddHHmmss.ffffff±UUU
        if (wmi is { Length: >= 14 } &&
            DateTimeOffset.TryParseExact(wmi[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var d))
            return d;
        return DateTimeOffset.MinValue;
    }

    private static string TypeName(int t) => t switch
    {
        0 => "Application install",
        1 => "Application uninstall",
        10 => "Device driver install",
        12 => "Modify settings",
        13 => "Cancelled operation",
        _ => "Restore point",
    };
}
