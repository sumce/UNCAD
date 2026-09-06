param(
    [string]$Configuration = "Release",
    [string]$AutoCADDir = "D:\Program Files\Autodesk\AutoCAD 2022",
    [switch]$SkipBundle,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if ($Configuration -ne "Release") {
    throw "Only the unified UNCAD Pro Release configuration is supported."
}

$buildTarget = "$root\UNCAD.slnx"
$buildArgs = @(
    "build", $buildTarget, "-c", $Configuration,
    "-p:AutoCADDir=$AutoCADDir"
)
if ($NoRestore) { $buildArgs += "--no-restore" }
$cleanArgs = @("clean", $buildTarget, "-c", $Configuration,
    "-p:AutoCADDir=$AutoCADDir")
& dotnet $cleanArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
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
    $webViewLoaderRelative = "runtimes\win-x64\native\WebView2Loader.dll"
    $webViewLoader = Join-Path $outputDir $webViewLoaderRelative
    if (-not (Test-Path -LiteralPath $webViewLoader -PathType Leaf)) {
        throw "WebView2 x64 loader is missing: $webViewLoader"
    }
    $bundleWebViewLoader = Join-Path $bundleDir $webViewLoaderRelative
    New-Item (Split-Path -Parent $bundleWebViewLoader) -ItemType Directory -Force | Out-Null
    Copy-Item -LiteralPath $webViewLoader -Destination $bundleWebViewLoader -Force

    $template = Join-Path $root "BOQ_Template.xlsx"
    if (-not (Test-Path -LiteralPath $template -PathType Leaf)) { throw "BOQ template is missing: $template" }
    Copy-Item -LiteralPath $template -Destination (Join-Path $bundleDir "BOQ_Template.xlsx") -Force
    $frameTemplate = Join-Path $outputDir "Resources\XFrameTemplate.dwg"
    if (-not (Test-Path -LiteralPath $frameTemplate -PathType Leaf)) { throw "xframe template is missing: $frameTemplate" }
    $bundleResources = Join-Path $bundleDir "Resources"
    if (-not (Test-Path $bundleResources)) { New-Item -ItemType Directory -Path $bundleResources -Force | Out-Null }
    Copy-Item -LiteralPath $frameTemplate -Destination (Join-Path $bundleResources "XFrameTemplate.dwg") -Force
    $bundlePayloadFiles = @(
        "PackageContents.xml", "UNCAD.dll", "NPOI.dll", "NPOI.OOXML.dll",
        "NPOI.OpenXml4Net.dll", "NPOI.OpenXmlFormats.dll",
        "ICSharpCode.SharpZipLib.dll", "BouncyCastle.Crypto.dll",
        "Microsoft.Web.WebView2.Core.dll", "Microsoft.Web.WebView2.WinForms.dll",
        "Microsoft.Web.WebView2.Wpf.dll", "runtimes\win-x64\native\WebView2Loader.dll",
        "BOQ_Template.xlsx", "Resources\XFrameTemplate.dwg"
    )
    $checksumPath = Join-Path $bundleDir "checksums.sha256"
    $actualPayloadFiles = @(Get-ChildItem -LiteralPath $bundleDir -File -Recurse |
        Where-Object { $_.FullName -ne $checksumPath } |
        ForEach-Object {
            $_.FullName.Substring($bundleDir.Length).TrimStart(
                [IO.Path]::DirectorySeparatorChar)
        })
    $missingPayloadFiles = @($bundlePayloadFiles | Where-Object {
        $actualPayloadFiles -notcontains $_
    })
    $unexpectedPayloadFiles = @($actualPayloadFiles | Where-Object {
        $bundlePayloadFiles -notcontains $_
    })
    if ($missingPayloadFiles.Count -gt 0) {
        throw "Bundle payload is missing: $($missingPayloadFiles -join ', ')"
    }
    if ($unexpectedPayloadFiles.Count -gt 0) {
        throw "Unexpected bundle payload file: $($unexpectedPayloadFiles -join ', ')"
    }
    $checksumLines = @($bundlePayloadFiles | Sort-Object | ForEach-Object {
        $relative = $_
        $fullPath = Join-Path $bundleDir $relative
        "{0}  {1}" -f (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash,
            $relative.Replace('\', '/')
    })
    [IO.File]::WriteAllLines($checksumPath, $checksumLines,
        (New-Object Text.UTF8Encoding($false)))
    Write-Host "Bundle updated: $bundleDir" -ForegroundColor Green
    Write-Host "Run release.ps1 to create the complete installer ZIP; users start with setup.bat." -ForegroundColor Yellow
}
