using System.Security.Cryptography;
using FluentAssertions;
using PowerX.Core.Diagnostics;
using Xunit;

namespace PowerX.Core.Tests;

public class UpdaterTests
{
    private static UpdateCheckResult WithInstaller(string url, string sha, long bytes) =>
        new(true, new Version(0, 1, 0), new Version(0, 2, 0), "notes", "https://github.com/Nowalski/Power-X/releases",
            null, InstallerUrl: url, InstallerSha256: sha, InstallerBytes: bytes);

    private static readonly string Sha64 = new('a', 64);

    [Fact]
    public void HasVerifiedInstaller_accepts_a_github_https_msi_with_a_64_hex_hash_and_size()
        => WithInstaller("https://github.com/Nowalski/Power-X/releases/download/v0.2.0/PowerX-Setup-0.2.0-win-x64.msi", Sha64, 1234)
            .HasVerifiedInstaller.Should().BeTrue();

    [Theory]
    [InlineData("http://github.com/x/y/z.msi")]                                  // not https
    [InlineData("https://example.com/PowerX-Setup.msi")]                         // not github
    [InlineData("https://raw.githubusercontent.com/Nowalski/PowerX/main/x.msi")] // wrong github host
    public void HasVerifiedInstaller_rejects_untrusted_or_insecure_urls(string url)
        => WithInstaller(url, Sha64, 1234).HasVerifiedInstaller.Should().BeFalse();

    [Fact]
    public void HasVerifiedInstaller_rejects_a_short_hash_or_zero_size()
    {
        WithInstaller("https://github.com/a/b/c.msi", "deadbeef", 1234).HasVerifiedInstaller.Should().BeFalse();
        WithInstaller("https://github.com/a/b/c.msi", Sha64, 0).HasVerifiedInstaller.Should().BeFalse();
    }

    [Fact]
    public async Task DownloadVerifiedAsync_refuses_when_there_is_no_verified_installer()
    {
        var noInstaller = new UpdateCheckResult(true, new Version(0,1,0), new Version(0,2,0), "n", "u", null);
        var r = await UpdateInstaller.DownloadVerifiedAsync(noInstaller);
        r.Ok.Should().BeFalse();
        r.Path.Should().BeNull();
    }

    [Fact]
    public void Launch_fails_cleanly_on_a_missing_file()
    {
        var r = UpdateInstaller.Launch(Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.msi"), Sha64);
        r.Success.Should().BeFalse();
    }

    [Fact]
    public void Launch_refuses_a_file_whose_hash_does_not_match()
    {
        string tmp = Path.Combine(Path.GetTempPath(), $"powerx-fake-{Guid.NewGuid():N}.msi");
        File.WriteAllText(tmp, "not really an installer");
        try
        {
            var r = UpdateInstaller.Launch(tmp, Sha64);
            r.Success.Should().BeFalse();
            r.Message.Should().Contain("hash");
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Manifest_without_installer_fields_is_not_a_verified_installer()
    {
        // The shipped version.json has empty installer fields until there is a real release.
        var r = new UpdateCheckResult(true, new Version(0,1,0), new Version(0,1,1), "n", "https://github.com/Nowalski/Power-X/releases", null);
        r.HasVerifiedInstaller.Should().BeFalse();
    }

    private static ReleaseManifest ManifestWithInstaller(int minBuild) => new()
    {
        Version = "0.2.0", Notes = "release notes",
        InstallerUrl = "https://github.com/Nowalski/Power-X/releases/download/v0.2.0/PowerX-Setup-0.2.0-win-x64.msi",
        InstallerSha256 = new string('a', 64), InstallerBytes = 5_000_000,
        MinimumWindowsBuild = minBuild,
    };

    [Fact]
    public void Build_offers_the_installer_when_the_OS_build_meets_the_minimum()
    {
        var r = UpdateChecker.Build(ManifestWithInstaller(22000), new Version(0, 1, 0), new Version(0, 2, 0), newer: true, thisBuild: 26100);
        r.HasVerifiedInstaller.Should().BeTrue();
        r.Notes.Should().Be("release notes");
    }

    [Fact]
    public void Build_withholds_the_installer_when_the_OS_is_older_than_the_minimum_build()
    {
        var r = UpdateChecker.Build(ManifestWithInstaller(26100), new Version(0, 1, 0), new Version(0, 2, 0), newer: true, thisBuild: 19045);
        r.UpdateAvailable.Should().BeTrue();          // still tell the user
        r.HasVerifiedInstaller.Should().BeFalse();    // but never one-click an MSI that won't run
        r.InstallerUrl.Should().BeNull();
        r.Notes.Should().Contain("26100").And.Contain("19045");
    }
}
