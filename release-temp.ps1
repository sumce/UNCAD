[CmdletBinding()]
param(
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$configuration = "Temporary"
$licenseMode = "Trial"
$licenseExpires = "2026-09-08T23:59:59+08:00"

$buildArgs = @{
    Configuration = $configuration
    SkipBundle = $true
}
if ($NoRestore) { $buildArgs.NoRestore = $true }
& (Join-Path $root "build.ps1") @buildArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$testProject = "$root\tests\UNCAD.Tests\UNCAD.Tests.csproj"
$testArgs = @("test", $testProject, "-c", $configuration, "--no-build")
if ($NoRestore) { $testArgs += "--no-restore" }
& dotnet $testArgs
if ($LASTEXITCODE -ne 0) { throw "Temporary release tests failed." }

$buildOutput = Join-Path $root "src\UNCAD\bin\$configuration\net48"
$module = Join-Path $buildOutput "UNCAD.dll"
if (-not (Test-Path $module -PathType Leaf)) { throw "Temporary build output is missing: $module" }

[xml]$sourceManifest = Get-Content (Join-Path $root "bundle\UNCAD.bundle\PackageContents.xml") -Raw -Encoding UTF8
$version = [string]$sourceManifest.ApplicationPackage.AppVersion
$baseName = "UNCAD-v$version-temp-20260908"
$artifacts = Join-Path $root "artifacts"
$stage = Join-Path $artifacts $baseName
$archive = Join-Path $artifacts ($baseName + ".zip")
$stageBundle = Join-Path $stage "bundle\UNCAD.bundle"
$distributionFiles = @(
    "setup.bat", "installer.ps1", "install-user.bat", "install-all.bat",
    "verify-install.bat", "uninstall.bat", "uninstall-all.bat", "README.md"
)

foreach ($file in $distributionFiles) {
    if (-not (Test-Path (Join-Path $root $file) -PathType Leaf)) {
        throw "Distribution file is missing: $file"
    }
}
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $archive) { Remove-Item $archive -Force }
New-Item (Join-Path $stage "bundle") -ItemType Directory -Force | Out-Null
Copy-Item (Join-Path $root "bundle\UNCAD.bundle") $stageBundle -Recurse -Force
Get-ChildItem (Join-Path $buildOutput "*.dll") | Copy-Item -Destination $stageBundle -Force

Copy-Item (Join-Path $root "BOQ_Template.xlsx") (Join-Path $stageBundle "BOQ_Template.xlsx") -Force
Copy-Item ($distributionFiles | ForEach-Object { Join-Path $root $_ }) $stage -Force

$manifestPath = Join-Path $stageBundle "PackageContents.xml"
[xml]$manifest = Get-Content $manifestPath -Raw -Encoding UTF8
$package = $manifest.ApplicationPackage
$package.SetAttribute("Name", "UNCAD Temporary")
$package.SetAttribute("LicenseMode", $licenseMode)
$package.SetAttribute("LicenseExpiresUtc", $licenseExpires)
$manifest.Save($manifestPath)

if ([string]$package.LicenseMode -ne $licenseMode -or
    [string]$package.LicenseExpiresUtc -ne $licenseExpires) {
    throw "Temporary package license metadata is inconsistent."
}
& (Join-Path $stage "installer.ps1") -Mode VerifyPackage
if ($LASTEXITCODE -ne 0) { throw "Temporary package validation failed." }

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash
Write-Host "Temporary release archive: $archive" -ForegroundColor Green
Write-Host "License expires: 2026-09-08 23:59:59 UTC+8" -ForegroundColor Cyan
Write-Host "SHA-256: $hash" -ForegroundColor Green
