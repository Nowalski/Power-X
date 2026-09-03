using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace PowerX.Core.Diagnostics;

public sealed record ReleaseManifest
{
    [JsonPropertyName("version")] public string Version { get; init; } = "";
    [JsonPropertyName("published")] public string Published { get; init; } = "";
    [JsonPropertyName("notes")] public string Notes { get; init; } = "";
    [JsonPropertyName("url")] public string Url { get; init; } = "";
    [JsonPropertyName("minimumWindowsBuild")] public int MinimumWindowsBuild { get; init; }

    // Optional: a signed-by-git MSI on the project's own Releases. Absent until there is one.
    [JsonPropertyName("installerUrl")] public string InstallerUrl { get; init; } = "";
    [JsonPropertyName("installerSha256")] public string InstallerSha256 { get; init; } = "";
    [JsonPropertyName("installerBytes")] public long InstallerBytes { get; init; }
}

public sealed record UpdateCheckResult(
    bool UpdateAvailable,
    Version? Current,
    Version? Latest,
    string? Notes,
    string? DownloadUrl,
    string? Error,
    string? InstallerUrl = null,
    string? InstallerSha256 = null,
    long InstallerBytes = 0)
{
    public static UpdateCheckResult Failed(string message) => new(false, null, null, null, null, message);

    /// <summary>A hash-pinned MSI on github.com/Nowalski/Power-X/releases is available for this update.</summary>
    public bool HasVerifiedInstaller =>
        !string.IsNullOrWhiteSpace(InstallerUrl) &&
        InstallerSha256 is { Length: 64 } &&
        InstallerBytes > 0 &&
        Uri.TryCreate(InstallerUrl, UriKind.Absolute, out var u) &&
        u.Scheme == Uri.UriSchemeHttps &&
        (u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) ||
         u.Host.Equals("objects.githubusercontent.com", StringComparison.OrdinalIgnoreCase));
}

/// <summary>
/// Checks a small <c>version.json</c> published in the public repo and compares it to the
/// running build. Only surfaces the result — the decision to download or install is the user's.
/// One network request, only when asked.
/// </summary>
public static class UpdateChecker
{
    public const string ManifestUrl = "https://raw.githubusercontent.com/Nowalski/Power-X/main/version.json";

    public static async Task<UpdateCheckResult> CheckAsync(Version currentVersion, CancellationToken ct = default)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PowerX-UpdateCheck");

            var manifest = await http.GetFromJsonAsync<ReleaseManifest>(ManifestUrl, ct);
            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Version))
                return UpdateCheckResult.Failed("The version manifest could not be read.");

            if (!Version.TryParse(manifest.Version, out var latest))
                return UpdateCheckResult.Failed($"Unrecognised version '{manifest.Version}'.");

            bool newer = latest > Normalise(currentVersion);
            return Build(manifest, currentVersion, latest, newer, Environment.OSVersion.Version.Build);
        }
        catch (OperationCanceledException)
        {
            return UpdateCheckResult.Failed("The update check timed out.");
        }
        catch (HttpRequestException ex)
        {
            return UpdateCheckResult.Failed($"Could not reach the update server: {ex.Message}");
        }
        catch (Exception ex)
        {
            return UpdateCheckResult.Failed(ex.Message);
        }
    }

    // AssemblyVersion is x.y.z.0 — compare on the first three components.
    private static Version Normalise(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    /// <summary>
    /// Turn a parsed manifest into a result. If the release needs a newer Windows build than this
    /// PC has, the user is still told an update exists, but the installer fields are dropped (so
    /// <see cref="UpdateCheckResult.HasVerifiedInstaller"/> is false) and the reason is spelled out:
    /// an MSI that will not run on this OS should never be offered as a one-click install.
    /// </summary>
    internal static UpdateCheckResult Build(ReleaseManifest manifest, Version current, Version latest, bool newer, int thisBuild)
    {
        bool osTooOld = manifest.MinimumWindowsBuild > 0 && thisBuild < manifest.MinimumWindowsBuild;

        string? notes = string.IsNullOrWhiteSpace(manifest.Notes) ? null : manifest.Notes;
        if (osTooOld)
            notes = $"This release needs Windows build {manifest.MinimumWindowsBuild} or newer (this PC is on {thisBuild}). "
                  + "Update Windows first, then install it from the releases page."
                  + (notes is null ? "" : "\n\n" + notes);

        return new UpdateCheckResult(
            newer, current, latest,
            notes,
            string.IsNullOrWhiteSpace(manifest.Url) ? null : manifest.Url,
            null,
            InstallerUrl: osTooOld || string.IsNullOrWhiteSpace(manifest.InstallerUrl) ? null : manifest.InstallerUrl,
            InstallerSha256: osTooOld || string.IsNullOrWhiteSpace(manifest.InstallerSha256) ? null : manifest.InstallerSha256.Trim().ToLowerInvariant(),
            InstallerBytes: osTooOld ? 0 : manifest.InstallerBytes);
    }
}
