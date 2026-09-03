using System.ServiceProcess;
using Microsoft.Win32;
using PowerX.Core.Processes;

namespace PowerX.Core.Services;

public enum ServiceStartMode2 { Boot, System, Automatic, AutomaticDelayed, Manual, Disabled, Unknown }

public sealed record ServiceEntry
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public string Description { get; init; } = "";
    public required ServiceControllerStatus Status { get; init; }
    public required ServiceStartMode2 StartMode { get; init; }
    public required bool CanStop { get; init; }
    public string Account { get; init; } = "";
    public string ImagePath { get; init; } = "";
    public bool IsCritical { get; init; }

    public string StatusText => Status switch
    {
        ServiceControllerStatus.Running => "Running",
        ServiceControllerStatus.Stopped => "Stopped",
        ServiceControllerStatus.Paused => "Paused",
        ServiceControllerStatus.StartPending => "Starting…",
        ServiceControllerStatus.StopPending => "Stopping…",
        _ => Status.ToString(),
    };

    public string StartModeText => StartMode switch
    {
        ServiceStartMode2.Automatic => "Automatic",
        ServiceStartMode2.AutomaticDelayed => "Automatic (delayed)",
        ServiceStartMode2.Manual => "Manual",
        ServiceStartMode2.Disabled => "Disabled",
        ServiceStartMode2.Boot => "Boot",
        ServiceStartMode2.System => "System",
        _ => "Unknown",
    };
}

/// <summary>
/// A modern front end for the common <c>services.msc</c> workflows. Start/stop go through
/// <see cref="ServiceController"/>; start-type changes are a registry write to the service's
/// <c>Start</c> value (reversible, what Autoruns/most tools do). Known-critical services are
/// flagged so the UI can warn before stopping them.
/// </summary>
public static class ServiceProvider
{
    private static readonly HashSet<string> Critical = new(StringComparer.OrdinalIgnoreCase)
    {
        "RpcSs", "RpcEptMapper", "DcomLaunch", "Dhcp", "Dnscache", "LSM", "Power", "PlugPlay",
        "BrokerInfrastructure", "SamSs", "Schedule", "EventLog", "ProfSvc", "gpsvc",
        "Winmgmt", "CryptSvc", "BFE", "mpssvc", "WinDefend", "wscsvc", "UserManager",
        "CoreMessagingRegistrar", "SystemEventsBroker", "nsi", "Netman", "NlaSvc", "netprofm",
        "AudioEndpointBuilder", "Audiosrv", "Themes", "ShellHWDetection", "TrustedInstaller",
    };

    public static IReadOnlyList<ServiceEntry> Enumerate()
    {
        var result = new List<ServiceEntry>();
        foreach (var sc in ServiceController.GetServices())
        {
            try
            {
                var (mode, account, image, description) = ReadConfig(sc.ServiceName);
                result.Add(new ServiceEntry
                {
                    Name = sc.ServiceName,
                    DisplayName = sc.DisplayName,
                    Description = description,
                    Status = SafeStatus(sc),
                    StartMode = mode,
                    CanStop = SafeCanStop(sc),
                    Account = account,
                    ImagePath = image,
                    IsCritical = Critical.Contains(sc.ServiceName),
                });
            }
            catch (Exception) { /* transient — skip this service */ }
            finally { sc.Dispose(); }
        }
        return result.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static ActionResult Start(string name) => Control(name, sc =>
    {
        if (sc.Status is ServiceControllerStatus.Running) return;
        sc.Start();
        sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(20));
    });

    public static ActionResult Stop(string name) => Control(name, sc =>
    {
        if (sc.Status is ServiceControllerStatus.Stopped) return;
        sc.Stop();
        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(20));
    });

    public static ActionResult Restart(string name)
    {
        var stop = Stop(name);
        if (!stop.Success && !stop.Message!.Contains("already", StringComparison.OrdinalIgnoreCase)) return stop;
        Thread.Sleep(500);
        return Start(name);
    }

    public static ActionResult SetStartMode(string name, ServiceStartMode2 mode)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}", writable: true);
            if (key is null) return ActionResult.Fail("Service not found.");

            int start = mode switch
            {
                ServiceStartMode2.Boot => 0,
                ServiceStartMode2.System => 1,
                ServiceStartMode2.Automatic or ServiceStartMode2.AutomaticDelayed => 2,
                ServiceStartMode2.Manual => 3,
                ServiceStartMode2.Disabled => 4,
                _ => 3,
            };
            key.SetValue("Start", start, RegistryValueKind.DWord);
            key.SetValue("DelayedAutoStart", mode == ServiceStartMode2.AutomaticDelayed ? 1 : 0, RegistryValueKind.DWord);
            return ActionResult.Ok;
        }
        catch (UnauthorizedAccessException) { return ActionResult.Fail("Administrator rights required."); }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
    }

    // ---------------------------------------------------------------- internals

    private static ActionResult Control(string name, Action<ServiceController> act)
    {
        try
        {
            using var sc = new ServiceController(name);
            act(sc);
            return ActionResult.Ok;
        }
        catch (InvalidOperationException ex) when (ex.InnerException is System.ComponentModel.Win32Exception w)
        {
            return ActionResult.Fail(w.NativeErrorCode == 5 ? "Windows denied access to this service." : w.Message);
        }
        catch (System.ServiceProcess.TimeoutException)
        {
            return ActionResult.Fail("The service did not respond in time.");
        }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
    }

    private static (ServiceStartMode2 mode, string account, string image, string description) ReadConfig(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{name}");
        if (key is null) return (ServiceStartMode2.Unknown, "", "", "");

        int start = key.GetValue("Start") is int s ? s : -1;
        bool delayed = key.GetValue("DelayedAutoStart") is 1;
        var mode = start switch
        {
            0 => ServiceStartMode2.Boot,
            1 => ServiceStartMode2.System,
            2 => delayed ? ServiceStartMode2.AutomaticDelayed : ServiceStartMode2.Automatic,
            3 => ServiceStartMode2.Manual,
            4 => ServiceStartMode2.Disabled,
            _ => ServiceStartMode2.Unknown,
        };

        string account = key.GetValue("ObjectName")?.ToString() ?? "";
        string image = key.GetValue("ImagePath")?.ToString() ?? "";
        string description = key.GetValue("Description")?.ToString() ?? "";
        if (description.StartsWith('@')) description = ""; // unresolved indirect string
        return (mode, account, image, description);
    }

    private static ServiceControllerStatus SafeStatus(ServiceController sc)
    {
        try { return sc.Status; }
        catch { return ServiceControllerStatus.Stopped; }
    }

    private static bool SafeCanStop(ServiceController sc)
    {
        try { return sc.CanStop; }
        catch { return false; }
    }
}
