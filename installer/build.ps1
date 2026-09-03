param(
    [string]$Configuration = "Release",
    [string]$Rid = "win-x64",
    [string]$Version = "",
    [string]$OutDir = ""
)

# Builds PowerX-Setup-<version>-<rid>.msi:
#   1. publishes PowerX.App self-contained (folder, with the bundled VC++ runtime)
#   2. runs `wix build` to harvest the folder into an MSI
#   3. prints the version.json fields (installerUrl / installerSha256 / installerBytes)
# Requires: .NET 10 SDK; `dotnet tool install -g wix --version 5.0.2`;
#           `wix extension add -g WixToolset.UI.wixext/5.0.2`.

$ErrorActionPreference = "Stop"
$repo = (Resolve-Path "$PSScriptRoot\..").Path
$app  = Join-Path $repo "src\PowerX.App\PowerX.App.csproj"
if (-not $OutDir) { $OutDir = Join-Path $repo "publish" }
$publishDir = Join-Path $OutDir "app"

if (-not $Version) {
    $m = Select-String -Path $app -Pattern '<Version>([^<]+)</Version>' -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($m) { $Version = $m.Matches.Groups[1].Value } else { $Version = "0.1.0" }
}
Write-Host "PowerX installer  version $Version  ($Rid $Configuration)" -ForegroundColor Cyan

# 1. publish
if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
dotnet publish $app -c $Configuration -r $Rid --self-contained true -p:Platform=x64 -p:PublishSingleFile=false -p:DebugType=none -p:DebugSymbols=false -o $publishDir --nologo
if ($LASTEXITCODE -ne 0) { throw "publish failed" }
if (-not (Test-Path (Join-Path $publishDir "PowerX.App.exe"))) { throw "PowerX.App.exe missing from publish" }
if (-not (Test-Path (Join-Path $publishDir "vcruntime140.dll"))) { Write-Warning "vcruntime140.dll not bundled - the MSI may not start on a machine without the VC++ redistributable." }

# 2. wix build
$msi = Join-Path $OutDir ("PowerX-Setup-" + $Version + "-" + $Rid + ".msi")
Push-Location $PSScriptRoot
try {
    wix build "PowerX.wxs" -define ("Version=" + $Version) -define ("PublishDir=" + $publishDir) -bindpath $PSScriptRoot -ext WixToolset.UI.wixext -arch x64 -o $msi
} finally { Pop-Location }
if ($LASTEXITCODE -ne 0) { throw "wix build failed" }

# 3. manifest fields
$hash = (Get-FileHash $msi -Algorithm SHA256).Hash.ToLower()
$size = (Get-Item $msi).Length
$mb   = [math]::Round(($size / 1048576), 1)
$name = Split-Path $msi -Leaf
Write-Host ""
Write-Host ("Built: " + $msi + "  (" + $mb + " MB)") -ForegroundColor Green
Write-Host "version.json fields:" -ForegroundColor Cyan
Write-Host ('  "installerUrl":    "https://github.com/Nowalski/Power-X/releases/download/v' + $Version + '/' + $name + '",')
Write-Host ('  "installerSha256": "' + $hash + '",')
Write-Host ('  "installerBytes":  ' + $size)
