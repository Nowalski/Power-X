using System.Diagnostics;
using System.Net.Http;
using System.Security.Cryptography;
using PowerX.Core.Processes;

namespace PowerX.Core.Diagnostics;

public sealed record DownloadResult(bool Ok, string? Path, string? Error);

/// <summary>
/// Downloads the update MSI named in <c>version.json</c>, verifies its size and SHA-256 against
/// the manifest, and launches it. It will only fetch from the project's own GitHub Releases over
/// HTTPS, and refuses to run anything whose hash or length does not match — so this is not
/// "download and run a mystery binary", it is the project's own hash-pinned release.
/// </summary>
public static class UpdateInstaller
{
    private static string CacheDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PowerX", "update");

    public static async Task<DownloadResult> DownloadVerifiedAsync(
        UpdateCheckResult update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        if (!update.HasVerifiedInstaller || update.InstallerUrl is null || update.InstallerSha256 is null)
            return new DownloadResult(false, null, "This update has no hash-pinned installer to download.");

        try
        {
            Directory.CreateDirectory(CacheDir);
            string dest = Path.Combine(CacheDir, $"PowerX-Setup-{update.Latest}.msi");

            // Drop any installer we cached for a different version — it's dead weight now.
            foreach (var stale in Directory.EnumerateFiles(CacheDir, "*.*"))
                if (!stale.Equals(dest, StringComparison.OrdinalIgnoreCase)) TryDelete(stale);

            // Reuse a previously verified download.
            if (File.Exists(dest) && new FileInfo(dest).Length == update.InstallerBytes &&
                await HashMatchesAsync(dest, update.InstallerSha256, ct))
                return new DownloadResult(true, dest, null);

            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("PowerX-Updater");

            using var resp = await http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();

            long total = resp.Content.Headers.ContentLength ?? update.InstallerBytes;
            if (total > 0 && total != update.InstallerBytes)
                return new DownloadResult(false, null, $"The download is {total} bytes but the manifest expects {update.InstallerBytes}. Aborting.");

            string tmp = dest + ".partial";
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, ct)) > 0)
                {
                    await fs.WriteAsync(buffer.AsMemory(0, n), ct);
                    read += n;
                    if (total > 0) progress?.Report(Math.Clamp((double)read / total, 0, 1));
                }
            }

            if (new FileInfo(tmp).Length != update.InstallerBytes)
            {
                TryDelete(tmp);
                return new DownloadResult(false, null, "The downloaded file's size does not match the manifest.");
            }
            if (!await HashMatchesAsync(tmp, update.InstallerSha256, ct))
            {
                TryDelete(tmp);
                return new DownloadResult(false, null, "The downloaded file's SHA-256 does not match the manifest. It was NOT run.");
            }

            TryDelete(dest);
            File.Move(tmp, dest);
            progress?.Report(1);
            return new DownloadResult(true, dest, null);
        }
        catch (OperationCanceledException)
        {
            return new DownloadResult(false, null, "The download was cancelled.");
        }
        catch (Exception ex)
        {
            return new DownloadResult(false, null, ex.Message);
        }
    }

    /// <summary>
    /// Launch the verified MSI. It carries the same UpgradeCode, so Windows Installer does an
    /// in-place major upgrade (and prompts for elevation itself). The caller should close PowerX
    /// right after — the running exe would otherwise block the file replace.
    ///
    /// The file's SHA-256 is checked again here, immediately before it runs: the download was
    /// verified earlier, but it sits in a user-writable folder in between, so re-checking closes
    /// the gap between "verified" and "executed" before we hand an installer elevation.
    /// </summary>
    public static ActionResult Launch(string msiPath, string expectedSha256)
    {
        if (!File.Exists(msiPath)) return ActionResult.Fail("The installer file is missing.");
        try
        {
            using var fs = File.OpenRead(msiPath);
            string actual = Convert.ToHexString(SHA256.HashData(fs));
            if (!actual.Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
                return ActionResult.Fail("The installer on disk no longer matches the verified hash. It was NOT run.");
        }
        catch (Exception ex)
        {
            return ActionResult.Fail("Could not re-verify the installer: " + ex.Message);
        }
        try
        {
            Process.Start(new ProcessStartInfo("msiexec.exe", $"/i \"{msiPath}\"") { UseShellExecute = true });
            return ActionResult.Ok;
        }
        catch (Exception ex)
        {
            return ActionResult.Fail(ex.Message);
        }
    }

    private static async Task<bool> HashMatchesAsync(string path, string expectedHex, CancellationToken ct)
    {
        await using var fs = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(fs, ct);
        return Convert.ToHexString(hash).Equals(expectedHex, StringComparison.OrdinalIgnoreCase);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { /* best effort */ }
    }
}
