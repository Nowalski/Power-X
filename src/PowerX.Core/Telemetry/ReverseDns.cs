using System.Collections.Concurrent;
using System.Net;

namespace PowerX.Core.Telemetry;

/// <summary>
/// On-demand reverse DNS for remote endpoints. Never called automatically: the Network page only
/// resolves after the user turns "Resolve names" on. Results (including failures) are cached for
/// the life of the process so the same address is never looked up twice.
/// </summary>
public static class ReverseDns
{
    private static readonly ConcurrentDictionary<string, string?> Cache = new();

    /// <summary>The cached hostname, or null if not resolved yet or the lookup failed / was skipped.</summary>
    public static string? Cached(string ip) => Cache.GetValueOrDefault(ip);

    /// <summary>True once a lookup for this address has finished (successfully or not), so callers
    /// do not retry an address that has no PTR record on every refresh.</summary>
    public static bool Attempted(string ip) => Cache.ContainsKey(ip);

    public static bool IsResolvable(string ip) =>
        IPAddress.TryParse(ip, out var a) &&
        !IPAddress.IsLoopback(a) && !a.Equals(IPAddress.Any) && !a.Equals(IPAddress.IPv6Any) &&
        !IsPrivateOrLinkLocal(a);

    public static async Task<string?> ResolveAsync(string ip, CancellationToken ct = default)
    {
        if (Cache.TryGetValue(ip, out var hit)) return hit;
        string? name = null;
        if (IsResolvable(ip) && IPAddress.TryParse(ip, out var addr))
        {
            try
            {
                var entry = await Dns.GetHostEntryAsync(addr).WaitAsync(TimeSpan.FromSeconds(3), ct);
                if (!string.Equals(entry.HostName, ip, StringComparison.OrdinalIgnoreCase))
                    name = entry.HostName;
            }
            catch (Exception) { name = null; }   // NXDOMAIN, timeout, no network: just show the IP
        }
        Cache[ip] = name;
        return name;
    }

    private static bool IsPrivateOrLinkLocal(IPAddress a)
    {
        if (a.IsIPv6LinkLocal || a.IsIPv6SiteLocal) return true;
        var b = a.GetAddressBytes();
        if (b.Length != 4) return false;
        return b[0] == 10
            || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
            || (b[0] == 192 && b[1] == 168)
            || (b[0] == 169 && b[1] == 254)   // link-local
            || (b[0] == 100 && b[1] >= 64 && b[1] <= 127);   // CGNAT
    }
}
