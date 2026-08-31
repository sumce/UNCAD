param(
    [string]$Configuration = "Release",
    [string]$AutoCADDir = "D:\Program Files\Autodesk\AutoCAD 2022",
    [switch]$SkipBundle,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$buildTarget = if ($Configuration -eq "Temporary") {
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
    $managedWebViewFiles = @(
        "Microsoft.Web.WebView2.Core.dll",
        "Microsoft.Web.WebView2.WinForms.dll",
        "Microsoft.Web.WebView2.Wpf.dll"
    )
    foreach ($file in $managedWebViewFiles) {
        if (-not (Test-Path (Join-Path $outputDir $file) -PathType Leaf)) {
            throw "WebView2 build output is missing: $file"
        }
    }
    Get-ChildItem "$outputDir\*.dll" | Copy-Item -Destination $bundleDir -Force

    $loaderRelative = "runtimes\win-x64\native\WebView2Loader.dll"
    $loaderSource = Join-Path $outputDir $loaderRelative
    $loaderDestination = Join-Path $bundleDir $loaderRelative
    if (-not (Test-Path $loaderSource -PathType Leaf)) {
        throw "WebView2 native loader is missing: $loaderSource"
    }
    New-Item -ItemType Directory -Path (Split-Path $loaderDestination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $loaderSource -Destination $loaderDestination -Force

    $webSource = Join-Path $root "src\UNCAD\Web\QuickLine3D"
    $webDestination = Join-Path $bundleDir "Web\QuickLine3D"
    if (-not (Test-Path $webSource -PathType Container)) {
        throw "QuickLine3D web source is missing: $webSource"
    }
    if (Test-Path $webDestination) {
        Remove-Item -LiteralPath $webDestination -Recurse -Force
    }
    New-Item -ItemType Directory -Path (Split-Path $webDestination -Parent) -Force | Out-Null
    Copy-Item -LiteralPath $webSource -Destination $webDestination -Recurse -Force

    $template = Join-Path $root "BOQ_Template.xlsx"
    if (-not (Test-Path -LiteralPath $template -PathType Leaf)) { throw "BOQ template is missing: $template" }
    Copy-Item -LiteralPath $template -Destination (Join-Path $bundleDir "BOQ_Template.xlsx") -Force
    Write-Host "Bundle updated: $bundleDir" -ForegroundColor Green
    Write-Host "Run release.ps1 to create the complete installer ZIP; users start with setup.bat." -ForegroundColor Yellow
}
