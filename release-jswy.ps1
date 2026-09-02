[CmdletBinding()]
param(
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$configuration = "JSWY"
$licenseMode = "Project"
$licenseExpires = "2026-10-01T00:00:00+08:00"
$customerCode = "UNCAD-JSWY"
# Keep this script parseable by Windows PowerShell 5.1 even when the repository is
# checked out without a UTF-8 BOM; construct the Chinese customer names by code point.
$licenseeCompany = [string]::Concat([char]0x6C5F, [char]0x82CF, [char]0x6587, [char]0x708E, [char]0x5EFA, [char]0x8BBE, [char]0x5DE5, [char]0x7A0B, [char]0x6709, [char]0x9650, [char]0x516C, [char]0x53F8)
$licenseeName = [string]::Concat([char]0x674E, [char]0x5C0F, [char]0x4EAE)
$expectedAuthorizationYears = "10"

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
if ($LASTEXITCODE -ne 0) { throw "JSWY release tests failed." }

$buildOutput = Join-Path $root "src\UNCAD\bin\$configuration\net48"
$module = Join-Path $buildOutput "UNCAD.dll"
if (-not (Test-Path $module -PathType Leaf)) { throw "JSWY build output is missing: $module" }

[xml]$sourceManifest = Get-Content (Join-Path $root "bundle\UNCAD.bundle\PackageContents.xml") -Raw -Encoding UTF8
$version = [string]$sourceManifest.ApplicationPackage.AppVersion
$baseName = "UNCAD-JSWY-v$version"
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
$customerDlls = @(Get-ChildItem (Join-Path $buildOutput "*.dll") -File)
$customerDllNames = @($customerDlls | ForEach-Object { $_.Name })
Get-ChildItem (Join-Path $stageBundle "*.dll") -File -ErrorAction SilentlyContinue |
    Where-Object { $customerDllNames -notcontains $_.Name } |
    Remove-Item -Force
$customerDlls | Copy-Item -Destination $stageBundle -Force
Copy-Item (Join-Path $root "BOQ_Template.xlsx") (Join-Path $stageBundle "BOQ_Template.xlsx") -Force
Copy-Item ($distributionFiles | ForEach-Object { Join-Path $root $_ }) $stage -Force

$manifestPath = Join-Path $stageBundle "PackageContents.xml"
[xml]$manifest = Get-Content $manifestPath -Raw -Encoding UTF8
$package = $manifest.ApplicationPackage
$package.SetAttribute("Name", $customerCode)
$package.SetAttribute("LicenseMode", $licenseMode)
$package.SetAttribute("LicenseExpiresUtc", $licenseExpires)
$package.SetAttribute("CustomerCode", $customerCode)
$package.SetAttribute("LicenseeCompany", $licenseeCompany)
$package.SetAttribute("LicenseeName", $licenseeName)
$package.SetAttribute("ExpectedAuthorizationYears", $expectedAuthorizationYears)
$manifest.Save($manifestPath)

if ([string]$package.Name -ne $customerCode -or
    [string]$package.LicenseMode -ne $licenseMode -or
    [string]$package.LicenseExpiresUtc -ne $licenseExpires -or
    [string]$package.CustomerCode -ne $customerCode -or
    [string]$package.LicenseeCompany -ne $licenseeCompany -or
    [string]$package.LicenseeName -ne $licenseeName -or
    [string]$package.ExpectedAuthorizationYears -ne $expectedAuthorizationYears) {
    throw "JSWY package license metadata is inconsistent."
}
& (Join-Path $stage "installer.ps1") -Mode VerifyPackage
if ($LASTEXITCODE -ne 0) { throw "JSWY package validation failed." }

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash
Write-Host "JSWY release archive: $archive" -ForegroundColor Green
Write-Host "Customer: $licenseeCompany / $licenseeName ($customerCode)" -ForegroundColor Cyan
Write-Host "License expires: 2026-10-01 00:00:00 UTC+8 (expected authorization: 10 years)" -ForegroundColor Cyan
Write-Host "SHA-256: $hash" -ForegroundColor Green
