using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace PowerX.Core.Diagnostics;

public enum FwDirection { In, Out }
public enum FwAction { Block, Allow }

public sealed record FirewallProfileState(bool DomainOn, bool PrivateOn, bool PublicOn)
{
    public bool AllOn => DomainOn && PrivateOn && PublicOn;
    public bool AnyOff => !DomainOn || !PrivateOn || !PublicOn;
}

public sealed record FirewallRule
{
    public required string Name { get; init; }
    public string Program { get; init; } = "";
    public string Service { get; init; } = "";
    public required FwDirection Direction { get; init; }
    public required FwAction Action { get; init; }
    public required bool Enabled { get; init; }
    public string LocalPorts { get; init; } = "";
    public string RemotePorts { get; init; } = "";
    public string Protocol { get; init; } = "";
    public bool Domain { get; init; }
    public bool Private { get; init; }
    public bool Public { get; init; }
    public string Grouping { get; init; } = "";

    /// <summary>App-scoped rules carry an owner SID or an app-container package id. They are created
    /// by an app for one user's sandbox and are normal — not an admin-configured open port.</summary>
    public bool IsAppScoped =>
        !string.IsNullOrEmpty(Owner)
        || AppContainer
        || Name.StartsWith("@{", StringComparison.Ordinal)
        || Grouping.Contains("ms-resource:", StringComparison.OrdinalIgnoreCase);

    public bool AppContainer { get; init; }
    public string Owner { get; init; } = "";

    /// <summary>An enabled inbound Allow rule that is reachable from an untrusted network, opens a
    /// port for any program, and was not created by an app for its own sandbox — i.e. a hole
    /// somebody deliberately punched. Worth confirming it is still needed.</summary>
    public bool WorthReviewing =>
        Enabled && Direction == FwDirection.In && Action == FwAction.Allow && Public
        && string.IsNullOrWhiteSpace(Program) && string.IsNullOrWhiteSpace(Service)
        && !IsAppScoped;
}

/// <summary>
/// Read-only view of the Windows Defender Firewall: whether it is on per profile, and the
/// inbound/outbound rules, with a flag on broad inbound-allow rules. PowerX does not add, change
/// or delete rules — this is a window onto what is already configured.
/// </summary>
[SupportedOSPlatform("windows")]
public static class FirewallRules
{
    public static FirewallProfileState ProfileState()
    {
        dynamic? policy = null;
        try
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type is null) return new FirewallProfileState(true, true, true);
            policy = Activator.CreateInstance(type);
            return new FirewallProfileState(
                (bool)policy!.FirewallEnabled[1],   // NET_FW_PROFILE2_DOMAIN
                (bool)policy.FirewallEnabled[2],    // NET_FW_PROFILE2_PRIVATE
                (bool)policy.FirewallEnabled[4]);   // NET_FW_PROFILE2_PUBLIC
        }
        catch (Exception)
        {
            return new FirewallProfileState(true, true, true);
        }
        finally { Release(policy); }
    }

    public static IReadOnlyList<FirewallRule> Rules()
    {
        var result = new List<FirewallRule>();
        dynamic? policy = null;
        try
        {
            var type = Type.GetTypeFromProgID("HNetCfg.FwPolicy2");
            if (type is null) return result;
            policy = Activator.CreateInstance(type);

            foreach (dynamic r in policy!.Rules)
            {
                try
                {
                    int profiles = SafeInt(() => r.Profiles);
                    result.Add(new FirewallRule
                    {
                        Name = SafeStr(() => r.Name),
                        Program = SafeStr(() => r.ApplicationName),
                        Service = SafeStr(() => r.serviceName),
                        AppContainer = !string.IsNullOrEmpty(SafeStr(() => r.LocalAppPackageId)),
                        Owner = SafeStr(() => r.LocalUserOwner),
                        Direction = SafeInt(() => r.Direction) == 2 ? FwDirection.Out : FwDirection.In,
                        Action = SafeInt(() => r.Action) == 1 ? FwAction.Allow : FwAction.Block,
                        Enabled = SafeBool(() => r.Enabled),
                        LocalPorts = SafeStr(() => r.LocalPorts),
                        RemotePorts = SafeStr(() => r.RemotePorts),
                        Protocol = ProtocolName(SafeInt(() => r.Protocol)),
                        Domain = (profiles & 1) != 0 || profiles == int.MaxValue,
                        Private = (profiles & 2) != 0 || profiles == int.MaxValue,
                        Public = (profiles & 4) != 0 || profiles == int.MaxValue,
                        Grouping = SafeStr(() => r.Grouping),
                    });
                }
                catch (Exception) { }
                finally { Release(r); }
            }
        }
        catch (Exception) { }
        finally { Release(policy); }

        return result
            .OrderByDescending(r => r.WorthReviewing)
            .ThenBy(r => r.Direction)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string ProtocolName(int p) => p switch
    {
        6 => "TCP", 17 => "UDP", 1 => "ICMPv4", 58 => "ICMPv6", 256 => "any", _ => p > 0 ? p.ToString() : "any",
    };

    private static string SafeStr(Func<object?> f) { try { return f()?.ToString() ?? ""; } catch { return ""; } }
    private static int SafeInt(Func<object?> f) { try { return Convert.ToInt32(f()); } catch { return 0; } }
    private static bool SafeBool(Func<object?> f) { try { return f() is bool b && b; } catch { return false; } }

    private static void Release(object? com)
    {
        if (com is not null && Marshal.IsComObject(com))
        {
            try { Marshal.FinalReleaseComObject(com); } catch (Exception) { }
        }
    }
}
