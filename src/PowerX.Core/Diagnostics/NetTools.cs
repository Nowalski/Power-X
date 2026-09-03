using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace PowerX.Core.Diagnostics;

/// <summary>Ping, traceroute and DNS lookup implemented with the managed networking stack.</summary>
public static class NetTools
{
    public static async Task PingAsync(string host, int count, Action<string> onLine, CancellationToken ct)
    {
        using var ping = new Ping();
        var buffer = Encoding.ASCII.GetBytes(new string('x', 32));
        long ok = 0, total = 0, sum = 0, min = long.MaxValue, max = 0;

        onLine($"Pinging {host} with 32 bytes of data:");
        for (int i = 0; i < count && !ct.IsCancellationRequested; i++)
        {
            try
            {
                var reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(4), buffer, cancellationToken: ct);
                total++;
                if (reply.Status == IPStatus.Success)
                {
                    ok++;
                    sum += reply.RoundtripTime;
                    min = Math.Min(min, reply.RoundtripTime);
                    max = Math.Max(max, reply.RoundtripTime);
                    onLine($"Reply from {reply.Address}: time={reply.RoundtripTime} ms  TTL={reply.Options?.Ttl}");
                }
                else
                {
                    onLine($"Request {reply.Status}");
                }
            }
            catch (PingException) when (i == 0)
            {
                onLine($"Could not ping {host}. Check the name or address is correct and that you're online.");
                return;
            }
            catch (Exception ex)
            {
                onLine($"Error: {ex.Message}");
            }
            try { await Task.Delay(700, ct); } catch (OperationCanceledException) { return; }
        }

        onLine("");
        onLine($"Sent {total}, received {ok}, lost {total - ok} ({(total == 0 ? 0 : (total - ok) * 100 / total)}% loss)");
        if (ok > 0) onLine($"Round-trip: min {min} ms, max {max} ms, avg {sum / ok} ms");
    }

    public static async Task TracerouteAsync(string host, Action<string> onLine, CancellationToken ct)
    {
        IPAddress target;
        try
        {
            var all = await Dns.GetHostAddressesAsync(host, ct);
            var v4 = all.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (v4 is null)
            {
                onLine(all.Length > 0
                    ? $"{host} only has an IPv6 address ({all[0]}). This traceroute is IPv4-only."
                    : $"{host} does not resolve to any address.");
                return;
            }
            target = v4;
        }
        catch (Exception ex) { onLine($"Could not resolve {host}: {ex.Message}"); return; }

        onLine($"Tracing route to {host} [{target}], max 30 hops:");
        using var ping = new Ping();
        var buffer = Encoding.ASCII.GetBytes(new string('x', 32));

        for (int ttl = 1; ttl <= 30 && !ct.IsCancellationRequested; ttl++)
        {
            var opts = new PingOptions(ttl, true);
            try
            {
                var reply = await ping.SendPingAsync(target, TimeSpan.FromSeconds(4), buffer, opts, ct);
                string name = "";
                if (reply.Address is not null && !reply.Address.Equals(IPAddress.Any))
                {
                    try { name = (await Dns.GetHostEntryAsync(reply.Address.ToString(), ct)).HostName; } catch { }
                }
                onLine($"{ttl,2}  {reply.RoundtripTime,4} ms  {reply.Address}{(name.Length > 0 ? $"  [{name}]" : "")}");
                if (reply.Status == IPStatus.Success) { onLine("\nTrace complete."); return; }
            }
            catch (Exception ex)
            {
                onLine($"{ttl,2}  *  {ex.Message}");
            }
        }
    }

    public static async Task DnsLookupAsync(string host, Action<string> onLine, CancellationToken ct)
    {
        host = host.Trim();
        if (host.Length == 0) { onLine("Enter a host name or IP address."); return; }

        // IP in → reverse (PTR) lookup only.
        if (IPAddress.TryParse(host, out var ip))
        {
            onLine($"Reverse lookup for {ip}:");
            try
            {
                var entry = await Dns.GetHostEntryAsync(ip).WaitAsync(ct);
                if (string.IsNullOrWhiteSpace(entry.HostName) || entry.HostName == ip.ToString())
                    onLine("  no PTR record (this IP has no reverse DNS name)");
                else
                    onLine($"  {entry.HostName}");
            }
            catch (SocketException)
            {
                onLine("  no PTR record (this IP has no reverse DNS name)");
            }
            catch (Exception ex)
            {
                onLine($"  lookup failed: {ex.Message}");
            }
            return;
        }

        // Name in → forward lookup (A / AAAA), then reverse-resolve each address.
        IPAddress[] addrs;
        try
        {
            addrs = await Dns.GetHostAddressesAsync(host, ct);
        }
        catch (SocketException ex) when (ex.SocketErrorCode is SocketError.HostNotFound or SocketError.NoData)
        {
            onLine($"{host}: name does not resolve (NXDOMAIN / no records).");
            return;
        }
        catch (Exception ex)
        {
            onLine($"Lookup failed: {ex.Message}");
            return;
        }

        if (addrs.Length == 0) { onLine($"{host}: no A or AAAA records."); return; }

        onLine($"{host} resolves to:");
        foreach (var a in addrs)
        {
            string kind = a.AddressFamily == AddressFamily.InterNetworkV6 ? "AAAA" : "A";
            onLine($"  {kind,-4} {a}");
        }

        onLine("");
        onLine("Reverse names:");
        foreach (var a in addrs)
        {
            if (ct.IsCancellationRequested) return;
            try
            {
                var entry = await Dns.GetHostEntryAsync(a).WaitAsync(ct);
                onLine($"  {a} → {(string.IsNullOrWhiteSpace(entry.HostName) ? "(none)" : entry.HostName)}");
            }
            catch (Exception)
            {
                onLine($"  {a} → (no PTR record)");
            }
        }
    }
}
