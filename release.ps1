[CmdletBinding()]
param(
    [string]$Suffix = "",
    [string]$Configuration = "Release",
    [switch]$NoBuild,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$bundle = Join-Path $root "bundle\UNCAD.bundle"

if (-not $NoBuild) {
    $buildArgs = @{ Configuration = $Configuration }
    if ($NoRestore) { $buildArgs.NoRestore = $true }
    & (Join-Path $root "build.ps1") @buildArgs
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

# 即使显式跳过构建，也禁止把旧输出或与 bundle 不一致的 DLL 打进正式产物。
$buildOutput = Join-Path $root ("src\UNCAD\bin\" + $Configuration + "\net48\UNCAD.dll")
$bundleModule = Join-Path $bundle "UNCAD.dll"
if (-not (Test-Path $buildOutput -PathType Leaf)) { throw "Build output is missing: $buildOutput" }
if ($NoBuild) {
    $newerInput = Get-ChildItem (Join-Path $root "src\UNCAD") -Recurse -File |
        Where-Object { $_.Extension -in @(".cs", ".csproj", ".tsv", ".svg") -and
            $_.LastWriteTimeUtc -gt (Get-Item $buildOutput).LastWriteTimeUtc } |
        Select-Object -First 1
    if ($newerInput) { throw "-NoBuild rejected: source is newer than UNCAD.dll ($($newerInput.FullName))." }
}
if (-not (Test-Path $bundleModule -PathType Leaf) -or
    (Get-FileHash $buildOutput -Algorithm SHA256).Hash -ne
    (Get-FileHash $bundleModule -Algorithm SHA256).Hash) {
    throw "Bundle UNCAD.dll does not match the verified build output. Run release.ps1 without -NoBuild."
}

& (Join-Path $root "installer.ps1") -Mode VerifyPackage
if ($LASTEXITCODE -ne 0) { throw "Release package validation failed." }

[xml]$manifest = Get-Content (Join-Path $bundle "PackageContents.xml") -Raw -Encoding UTF8
$version = [string]$manifest.ApplicationPackage.AppVersion
$temporaryPolicy = Join-Path $root "src\UNCAD\Core\Licensing\TemporaryLicensePolicy.cs"
if ([string]::IsNullOrWhiteSpace($Suffix) -and (Test-Path $temporaryPolicy)) {
    $Suffix = "temp-20260927"
}
$baseName = "UNCAD-v" + $version
if (-not [string]::IsNullOrWhiteSpace($Suffix)) { $baseName += "-" + $Suffix.Trim("-") }
$artifacts = Join-Path $root "artifacts"
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
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $archive -CompressionLevel Optimal
$hash = (Get-FileHash $archive -Algorithm SHA256).Hash
Write-Host "Release archive: $archive" -ForegroundColor Green
Write-Host "SHA-256: $hash" -ForegroundColor Green
Write-Host "Installer files: $($distributionFiles.Count)" -ForegroundColor Cyan
