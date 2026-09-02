param(
    [string]$Configuration = "Release",
    [string]$AutoCADDir = "D:\Program Files\Autodesk\AutoCAD 2022",
    [switch]$SkipBundle,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$buildTarget = if ($Configuration -in @("Temporary", "JSWY")) {
    "$root\tests\UNCAD.Tests\UNCAD.Tests.csproj"
} else {
    "$root\UNCAD.slnx"
}
$buildArgs = @(
    "build", $buildTarget, "-c", $Configuration,
    "-p:AutoCADDir=$AutoCADDir"
)
if ($NoRestore) { $buildArgs += "--no-restore" }
& dotnet $buildArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$outputDir = "$root\src\UNCAD\bin\$Configuration\net48"
$dll = Join-Path $outputDir "UNCAD.dll"
Write-Host "Build succeeded: $dll" -ForegroundColor Green

if (-not $SkipBundle) {
    $bundleDir = "$root\bundle\UNCAD.bundle"
    if (-not (Test-Path $bundleDir)) { New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null }
    $outputDlls = @(Get-ChildItem "$outputDir\*.dll" -File)
    $outputDllNames = @($outputDlls | ForEach-Object { $_.Name })
    # Keep the bundle's DLL set identical to the verified build output. This removes
    # dependencies left by deleted features (for example the old OpenTK editor).
    Get-ChildItem "$bundleDir\*.dll" -File -ErrorAction SilentlyContinue |
        Where-Object { $outputDllNames -notcontains $_.Name } |
        Remove-Item -Force
    $outputDlls | Copy-Item -Destination $bundleDir -Force

    $template = Join-Path $root "BOQ_Template.xlsx"
    if (-not (Test-Path -LiteralPath $template -PathType Leaf)) { throw "BOQ template is missing: $template" }
    Copy-Item -LiteralPath $template -Destination (Join-Path $bundleDir "BOQ_Template.xlsx") -Force
    Write-Host "Bundle updated: $bundleDir" -ForegroundColor Green
    Write-Host "Run release.ps1 to create the complete installer ZIP; users start with setup.bat." -ForegroundColor Yellow
}
