using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PowerX.Core.Processes;
using Windows.Management.Deployment;

namespace PowerX.Core.Debloat;

public sealed record InstalledApp
{
    public required string DisplayName { get; init; }
    public required string PackageFamilyName { get; init; }
    public required string PackageFullName { get; init; }
    public required string Publisher { get; init; }
    public required string Version { get; init; }
    public required bool IsFramework { get; init; }
    public required bool NonRemovable { get; init; }
    public DateTimeOffset? InstalledDate { get; init; }

    /// <summary>The catalog's take on this package, if we have curated it.</summary>
    public DebloatEntry? Catalog { get; init; }

    public string Category => Catalog?.Category ?? "Other installed";
    public RemovalClass Class => Catalog?.Class ?? RemovalClass.Advanced;
}

/// <summary>
/// Enumerates installed MSIX/Appx packages for the current user and maps them onto
/// <see cref="DebloatCatalog"/>. Removal is a structured, per-package operation with an
/// explicit confirmation upstream — never a bulk "remove all".
/// </summary>
public sealed class AppInventory
{
    private readonly ILogger _log;
    private readonly PackageManager _pm = new();

    public AppInventory(ILogger<AppInventory>? log = null) => _log = log ?? NullLogger<AppInventory>.Instance;

    public IReadOnlyList<InstalledApp> Enumerate(bool includeFrameworks = false)
    {
        var result = new List<InstalledApp>();
        foreach (var pkg in _pm.FindPackagesForUser(string.Empty))
        {
            bool isFramework = SafeBool(() => pkg.IsFramework);
            if (isFramework && !includeFrameworks) continue;

            string family = pkg.Id.FamilyName ?? "";
            var entry = DebloatCatalog.Match(family);

            result.Add(new InstalledApp
            {
                DisplayName = FriendlyName(pkg, entry, family),
                PackageFamilyName = family,
                PackageFullName = pkg.Id.FullName ?? "",
                Publisher = pkg.Id.Publisher ?? "",
                Version = FormatVersion(pkg),
                IsFramework = isFramework,
                NonRemovable = SafeBool(() => pkg.SignatureKind == Windows.ApplicationModel.PackageSignatureKind.System) || entry?.Class == RemovalClass.KeepSystem,
                InstalledDate = SafeDate(() => pkg.InstalledDate),
                Catalog = entry,
            });
        }
        return result
            .GroupBy(a => a.PackageFamilyName)
            .Select(g => g.First())
            .OrderBy(a => a.Category)
            .ThenBy(a => a.DisplayName)
            .ToList();
    }

    /// <summary>
    /// Remove one package. When <paramref name="allUsers"/> is set (and PowerX is elevated) this
    /// also removes it for every account and deprovisions it so it does not return for new users —
    /// the equivalent of DISM /Remove-ProvisionedAppxPackage.
    /// </summary>
    public async Task<ActionResult> RemoveAsync(string packageFullName, string packageFamilyName, bool allUsers = true)
    {
        try
        {
            var options = allUsers ? RemovalOptions.RemoveForAllUsers : RemovalOptions.None;
            var result = await _pm.RemovePackageAsync(packageFullName, options).AsTask();

            if (result.ExtendedErrorCode is not null)
                return ActionResult.Fail(FriendlyError(result.ExtendedErrorCode.HResult, result.ErrorText));

            if (allUsers && !string.IsNullOrEmpty(packageFamilyName))
            {
                try { await _pm.DeprovisionPackageForAllUsersAsync(packageFamilyName).AsTask(); }
                catch (Exception ex) { _log.LogInformation(ex, "Deprovision {Fam} skipped", packageFamilyName); }
            }
            return ActionResult.Ok;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Remove {Pkg} failed", packageFullName);
            return ActionResult.Fail(FriendlyError(ex.HResult, ex.Message));
        }
    }

    private static string FriendlyError(int hresult, string fallback) => (uint)hresult == 0x80073CFA
        ? "Windows blocked removal. This package is marked as required by the operating system."
        : string.IsNullOrWhiteSpace(fallback) ? $"0x{hresult:X8}" : fallback;

    private static string FriendlyName(Windows.ApplicationModel.Package pkg, DebloatEntry? entry, string family)
    {
        if (entry is not null) return entry.DisplayName;
        string? dn = SafeStr(() => pkg.DisplayName);
        if (!string.IsNullOrWhiteSpace(dn) && dn != family) return dn!;
        string name = pkg.Id.Name ?? family;
        int dot = name.IndexOf('.');
        return dot > 0 && dot < name.Length - 1 ? name[(dot + 1)..] : name;
    }

    private static string FormatVersion(Windows.ApplicationModel.Package pkg)
    {
        try { var v = pkg.Id.Version; return $"{v.Major}.{v.Minor}.{v.Build}.{v.Revision}"; }
        catch { return ""; }
    }

    private static bool SafeBool(Func<bool> f) { try { return f(); } catch { return false; } }
    private static string? SafeStr(Func<string> f) { try { return f(); } catch { return null; } }
    private static DateTimeOffset? SafeDate(Func<DateTimeOffset> f) { try { return f(); } catch { return null; } }
}
