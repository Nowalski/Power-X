using System.Runtime.Versioning;
using Microsoft.Management.Infrastructure;
using PowerX.Core.Processes;

namespace PowerX.Core.Telemetry;

public sealed record NetworkAdapterState
{
    /// <summary>The interface GUID — matches <see cref="System.Net.NetworkInformation.NetworkInterface.Id"/>,
    /// so the live rate/name data from <see cref="NetworkMetricsProvider"/> can be lined up with this.</summary>
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required bool Enabled { get; init; }
}

/// <summary>
/// Reads and toggles whether a network adapter is administratively enabled, via the same
/// <c>MSFT_NetAdapter</c> CIM class (<c>root\StandardCimv2</c>) the <c>Enable-NetAdapter</c> /
/// <c>Disable-NetAdapter</c> PowerShell cmdlets use. This is the same action as right-clicking the
/// adapter in Control Panel's Network Connections and choosing Enable/Disable: it does not change
/// any configuration, just whether the adapter is allowed to be up at all.
///
/// Deliberately uses <c>Microsoft.Management.Infrastructure</c> (CIM), not <c>System.Management</c>
/// (classic WMI): <c>MSFT_NetAdapter</c> is a native CIM/MI class, and querying it through the
/// legacy DCOM-based WMI client silently returns the base <c>CIM_NetworkPort</c> shape instead —
/// no error, just none of this class's own properties (Name, InterfaceGuid, InterfaceAdminStatus)
/// actually populated. Confirmed by direct comparison against both a raw WQL property-list query
/// (fails outright: "Invalid query") and <c>Get-NetAdapter</c>'s own values.
/// </summary>
[SupportedOSPlatform("windows")]
public static class NetworkAdapterControl
{
    private const string Namespace = @"root\StandardCimv2";

    public static IReadOnlyList<NetworkAdapterState> List()
    {
        var list = new List<NetworkAdapterState>();
        try
        {
            using var session = CimSession.Create(null);
            foreach (var inst in session.QueryInstances(Namespace, "WQL", "SELECT * FROM MSFT_NetAdapter"))
            {
                using (inst)
                {
                    string id = inst.CimInstanceProperties["InterfaceGuid"]?.Value as string ?? "";
                    if (id.Length == 0) continue;
                    // NET_IF_ADMIN_STATUS: 1 = Up (enabled), 2 = Down (disabled), 3 = Testing.
                    uint adminStatus = inst.CimInstanceProperties["InterfaceAdminStatus"]?.Value is { } v ? Convert.ToUInt32(v) : 1;
                    list.Add(new NetworkAdapterState
                    {
                        Id = id,
                        Name = inst.CimInstanceProperties["Name"]?.Value as string ?? "",
                        Description = inst.CimInstanceProperties["InterfaceDescription"]?.Value as string ?? "",
                        Enabled = adminStatus != 2,
                    });
                }
            }
        }
        catch (CimException) { /* CIM unavailable */ }
        return list;
    }

    public static ActionResult SetEnabled(string interfaceGuid, bool enabled)
    {
        if (string.IsNullOrWhiteSpace(interfaceGuid) || interfaceGuid.Any(c => c is '\'' or '\\'))
            return ActionResult.Fail("Invalid adapter.");
        try
        {
            using var session = CimSession.Create(null);
            using var instances = session.QueryInstances(Namespace, "WQL",
                $"SELECT * FROM MSFT_NetAdapter WHERE InterfaceGuid='{interfaceGuid}'").GetEnumerator();
            if (!instances.MoveNext())
                return ActionResult.Fail("Adapter not found. It may have been removed or renamed.");

            using var result = session.InvokeMethod(Namespace, instances.Current, enabled ? "Enable" : "Disable", null);
            return ActionResult.Ok;
        }
        catch (CimException cex) { return ActionResult.Fail(cex.Message); }
        catch (UnauthorizedAccessException) { return ActionResult.Fail("Administrator rights required."); }
        catch (Exception ex) { return ActionResult.Fail(ex.Message); }
    }
}
