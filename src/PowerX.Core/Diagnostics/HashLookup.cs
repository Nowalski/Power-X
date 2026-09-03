using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace PowerX.Core.Diagnostics;

public sealed record HashResult(
    bool Found,
    int? Trust,
    string? FileName,
    IReadOnlyList<string> Sources,
    bool KnownMalicious,
    string? MaliciousDetail,
    string Summary,
    string? Error = null);

/// <summary>
/// Looks a file's SHA-256 up against the CIRCL hash lookup service
/// (<c>https://hashlookup.circl.lu</c>): a free, open, no-key database of tens of billions of
/// known files from clean software sources (NSRL and others). It answers "is this a known,
/// catalogued file", not "is this malware". Only the hash is sent, over HTTPS, and only when the
/// caller asks. Results are cached for the life of the process.
///
/// PowerX is not an antivirus. A "not found" result is not a verdict, and a "known good" result
/// does not override what your antivirus says.
/// </summary>
public static class HashLookup
{
    public const string Endpoint = "https://hashlookup.circl.lu/lookup/sha256/";

    private static readonly ConcurrentDictionary<string, HashResult> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static HashResult? Cached(string sha256) => Cache.GetValueOrDefault(sha256);

    public static async Task<string> Sha256FileAsync(string path, CancellationToken ct = default)
    {
        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 20, useAsync: true);
        byte[] hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static async Task<HashResult> CheckAsync(string sha256, CancellationToken ct = default)
    {
        sha256 = sha256.Trim().ToLowerInvariant();
        if (sha256.Length != 64 || !IsHex(sha256))
            return new HashResult(false, null, null, [], false, null, "That is not a SHA-256 hash.", "bad hash");
        if (Cache.TryGetValue(sha256, out var hit)) return hit;

        HashResult result;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PowerX-HashLookup");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/json");

            using var resp = await http.GetAsync(Endpoint + sha256, ct);
            if (resp.StatusCode == HttpStatusCode.NotFound)
            {
                result = new HashResult(false, null, null, [], false, null,
                    "Not in the CIRCL known-file database. That is not proof of anything: many legitimate and many "
                    + "malicious files are simply not catalogued. If your antivirus flags this file, trust the antivirus.");
            }
            else
            {
                resp.EnsureSuccessStatusCode();
                await using var stream = await resp.Content.ReadAsStreamAsync(ct);
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                result = Parse(doc.RootElement);
            }
        }
        catch (OperationCanceledException)
        {
            return new HashResult(false, null, null, [], false, null, "The lookup was cancelled.", "cancelled");
        }
        catch (Exception ex)
        {
            return new HashResult(false, null, null, [], false, null,
                "Could not reach the CIRCL hash lookup service.", ex.Message);
        }

        Cache[sha256] = result;
        return result;
    }

    internal static HashResult Parse(JsonElement root)
    {
        string? fileName = Str(root, "FileName");
        int? trust = null;
        if (root.TryGetProperty("hashlookup:trust", out var t) && t.TryGetInt32(out var ti)) trust = ti;

        var sources = new List<string>();
        if (root.TryGetProperty("source", out var src))
        {
            if (src.ValueKind == JsonValueKind.String && src.GetString() is { } ss) sources.Add(ss);
            else if (src.ValueKind == JsonValueKind.Array)
                foreach (var e in src.EnumerateArray())
                    if (e.GetString() is { } es) sources.Add(es);
        }
        if (root.TryGetProperty("nsrl:MfgName", out var mfg) && mfg.GetString() is { } m && !sources.Contains(m)) sources.Add(m);

        bool malicious = false;
        string? malDetail = null;
        if (root.TryGetProperty("KnownMalicious", out var km))
        {
            malicious = true;
            malDetail = km.ValueKind == JsonValueKind.String ? km.GetString()
                : km.ValueKind == JsonValueKind.Array ? string.Join(", ", km.EnumerateArray().Select(e => e.GetString()))
                : "listed on a blocklist";
        }
        if (root.TryGetProperty("hashlookup:trust", out var tr) && tr.TryGetInt32(out var trv) && trv <= 20)
        {
            // very low trust on its own is not "malicious", but say so
        }

        string summary = malicious
            ? $"Flagged as malicious ({malDetail}). Treat this file as dangerous, and run a full antivirus scan."
            : trust is >= 50
                ? $"Known good file{(sources.Count > 0 ? $", seen in {string.Join(", ", sources.Take(4))}" : "")}. Trust {trust}/100."
                : trust is not null
                    ? $"Known file with low trust ({trust}/100){(sources.Count > 0 ? $", seen in {string.Join(", ", sources.Take(4))}" : "")}. Worth a closer look."
                    : $"Known file{(sources.Count > 0 ? $", seen in {string.Join(", ", sources.Take(4))}" : "")}.";

        return new HashResult(true, trust, fileName, sources, malicious, malDetail, summary);
    }

    private static string? Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool IsHex(string s)
    {
        foreach (char c in s)
            if (!Uri.IsHexDigit(c)) return false;
        return true;
    }
}
