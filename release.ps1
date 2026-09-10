[CmdletBinding()]
param(
    [string]$Suffix = "",
    [string]$Configuration = "Release",
    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ($Configuration -ne "Release") { throw "Only the unified UNCAD Pro Release configuration is supported." }
$productName = "UNCAD Pro"
$bundle = Join-Path $root "bundle\UNCAD.bundle"

function Assert-FileMatches {
    param([string]$Expected, [string]$Actual, [string]$Label)
    if (-not (Test-Path $Expected -PathType Leaf)) { throw "Build output is missing: $Label" }
    if (-not (Test-Path $Actual -PathType Leaf)) { throw "Bundle file is missing: $Label" }
    if ((Get-FileHash -LiteralPath $Expected -Algorithm SHA256).Hash -ne
        (Get-FileHash -LiteralPath $Actual -Algorithm SHA256).Hash) {
        throw "Bundle file does not match the verified build output: $Label"
    }
}

function Assert-TreeMatches {
    param([string]$ExpectedRoot, [string]$ActualRoot, [string]$Label,
        [string]$Filter = "*")
    if (-not (Test-Path $ExpectedRoot -PathType Container)) { throw "Build output folder is missing: $Label" }
    if (-not (Test-Path $ActualRoot -PathType Container)) { throw "Bundle folder is missing: $Label" }
    $expectedFiles = @(Get-ChildItem -LiteralPath $ExpectedRoot -File -Recurse -Filter $Filter)
    $actualFiles = @(Get-ChildItem -LiteralPath $ActualRoot -File -Recurse -Filter $Filter)
    if ($expectedFiles.Count -ne $actualFiles.Count) {
        throw "Bundle folder file count mismatch: $Label"
    }
    foreach ($sourceFile in $expectedFiles) {
        $relative = $sourceFile.FullName.Substring($ExpectedRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
        Assert-FileMatches $sourceFile.FullName (Join-Path $ActualRoot $relative) (Join-Path $Label $relative)
    }
}

if (-not $NoBuild) {
    $buildArgs = @{ Configuration = $Configuration }
    if ($NoRestore) { $buildArgs.NoRestore = $true }
    & (Join-Path $root "build.ps1") @buildArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# 即使显式跳过构建，也禁止针对旧输出运行测试或打包。
$buildOutput = Join-Path $root ("src\UNCAD\bin\" + $Configuration + "\net48\UNCAD.dll")
$bundleModule = Join-Path $bundle "UNCAD.dll"
$buildFrameTemplate = Join-Path (Split-Path -Parent $buildOutput) "Resources\XFrameTemplate.dwg"
$bundleFrameTemplate = Join-Path $bundle "Resources\XFrameTemplate.dwg"
if (-not (Test-Path $buildOutput -PathType Leaf)) { throw "Build output is missing: $buildOutput" }
if ($NoBuild) {
    $sourceInputs = @(Get-ChildItem (Join-Path $root "src\UNCAD") -Recurse -File |
        Where-Object { $_.Extension -in @(".cs", ".csproj", ".tsv", ".svg", ".xlsx", ".dwg", ".html", ".css", ".js", ".txt") })
    $sourceInputs += @(Get-Item (Join-Path $root "BOQ_Template.xlsx"))
    # Scripts and manifests affect the package without touching UNCAD.dll;
    # their staleness must also reject a -NoBuild shortcut.
    $sourceInputs += @(Get-ChildItem $root -File |
        Where-Object { $_.Extension -in @(".ps1", ".xml") })
    $sourceInputs += @(Get-Item (Join-Path $root "docs.html"))
    $sourceInputs += @(Get-ChildItem (Join-Path $root "bundle") -Recurse -File |
        Where-Object { $_.Extension -in @(".xml", ".ps1") })
    $newerInput = $sourceInputs |
        Where-Object { $_.LastWriteTimeUtc -gt (Get-Item $buildOutput).LastWriteTimeUtc } |
        Select-Object -First 1
    if ($newerInput) { throw "-NoBuild rejected: source is newer than UNCAD.dll ($($newerInput.FullName))." }
}
if (-not (Test-Path $bundleModule -PathType Leaf) -or
    (Get-FileHash $buildOutput -Algorithm SHA256).Hash -ne
    (Get-FileHash $bundleModule -Algorithm SHA256).Hash) {
    throw "Bundle UNCAD.dll does not match the verified build output. Run release.ps1 without -NoBuild."
}
Assert-FileMatches $buildFrameTemplate $bundleFrameTemplate "Resources\XFrameTemplate.dwg"
Assert-TreeMatches (Split-Path -Parent $buildOutput) $bundle "DLL payload" "*.dll"

$testArgs = @("test", "$root\UNCAD.slnx", "-c", $Configuration)
# build.ps1 already built the solution; avoid a second timestamped DLL build.
$testArgs += "--no-build"
if ($NoRestore) { $testArgs += "--no-restore" }
& dotnet $testArgs
if ($LASTEXITCODE -ne 0) { throw "Release tests failed." }

& (Join-Path $root "installer.ps1") -Mode VerifyPackage
if ($LASTEXITCODE -ne 0) { throw "Release package validation failed." }

[xml]$manifest = Get-Content (Join-Path $bundle "PackageContents.xml") -Raw -Encoding UTF8
$package = $manifest.ApplicationPackage
$version = [string]$package.AppVersion
if ([string]$package.Name -ne $productName -or
    [string]$package.LicenseMode -ne "Online" -or
    -not [string]::IsNullOrWhiteSpace([string]$package.LicenseExpiresUtc)) {
    throw "Unified Pro package metadata is inconsistent."
}
# D-010: the user-facing version label must agree with the bundle manifest and
# its release note. The assembly, the change log, and the test suite enforce
# their own halves of the contract; this is the join release.ps1 owns, so a
# single edited source cannot ship a package whose reported version drifts from
# the running application.
$metadataSource = Join-Path $root "src\UNCAD\Infra\ProductMetadata.cs"
$labelMatch = [regex]::Match(
    (Get-Content -LiteralPath $metadataSource -Raw -Encoding UTF8),
    'VersionLabel\s*=\s*"([^"]+)"')
if (-not $labelMatch.Success) {
    throw "ProductMetadata.VersionLabel was not found in $metadataSource."
}
$versionLabel = $labelMatch.Groups[1].Value
if ($version -ne $versionLabel -and $version -ne ($versionLabel + ".0")) {
    throw "Release version mismatch: ProductMetadata.VersionLabel='$versionLabel' does not agree with PackageContents.xml AppVersion='$version'."
}
$releaseNote = Join-Path $root ("docs\RELEASE-" + $versionLabel + ".md")
if (-not (Test-Path $releaseNote -PathType Leaf)) {
    throw "Release note document is missing: docs\RELEASE-$versionLabel.md (see docs/RELEASE.md)."
}
if ([IO.File]::ReadAllText($releaseNote).Trim().Length -eq 0) {
    throw "Release note document is empty: docs\RELEASE-$versionLabel.md"
}
$baseName = "UNCAD-Pro-v" + $version
if (-not [string]::IsNullOrWhiteSpace($Suffix)) { $baseName += "-" + $Suffix.Trim("-") }
$artifacts = Join-Path $root "artifacts\Pro"
$stage = Join-Path $artifacts $baseName
$archive = Join-Path $artifacts ($baseName + ".zip")
$distributionFiles = @(
    "setup.bat", "installer.ps1", "install-user.bat", "install-all.bat",
    "verify-install.bat", "uninstall.bat", "uninstall-all.bat", "README.md"
)
foreach ($file in $distributionFiles) {
    if (-not (Test-Path (Join-Path $root $file) -PathType Leaf)) { throw "Distribution file is missing: $file" }
}
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
if (Test-Path $archive) { Remove-Item $archive -Force }
New-Item (Join-Path $stage "bundle") -ItemType Directory -Force | Out-Null
Copy-Item $bundle (Join-Path $stage "bundle\UNCAD.bundle") -Recurse -Force
Copy-Item ($distributionFiles | ForEach-Object { Join-Path $root $_ }) $stage -Force

& (Join-Path $stage "installer.ps1") -Mode VerifyPackage
if ($LASTEXITCODE -ne 0) { throw "Unified Pro package validation failed." }

Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $archive -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash
Write-Host "UNCAD Pro release archive: $archive" -ForegroundColor Green
Write-Host "Customer and expiry metadata are supplied by pro.key." -ForegroundColor Cyan
Write-Host "SHA-256: $hash" -ForegroundColor Green
Write-Host "Installer files: $($distributionFiles.Count)" -ForegroundColor Cyan
