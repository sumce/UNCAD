param(
    [string]$AutoCADDir = 'D:\Program Files\Autodesk\AutoCAD 2022',
    [string]$InputDrawing,
    [string]$Command = 'UNCAD_COLOR_SELFTEST'
)

$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $projectDir '..\..')).Path
if ([string]::IsNullOrWhiteSpace($InputDrawing)) {
    $InputDrawing = Join-Path $repoRoot 'UNCAD.dwg'
}
$console = Join-Path $AutoCADDir 'accoreconsole.exe'
$project = Join-Path $projectDir 'UNCAD.CadIntegration.csproj'
$assembly = Join-Path $projectDir 'bin\Release\net48\UNCAD.CadIntegration.dll'
if (!(Test-Path -LiteralPath $console)) { throw "AutoCAD Core Console not found: $console" }
if (!(Test-Path -LiteralPath $InputDrawing)) { throw "Input drawing not found: $InputDrawing" }

dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'CAD integration fixture build failed.' }

$runId = [Guid]::NewGuid().ToString('N')
$tempDir = [System.IO.Path]::GetTempPath()
$scriptPath = Join-Path $tempDir "uncad-cad-$runId.scr"
$resultPath = Join-Path $tempDir "uncad-cad-$runId.result"
$drawingPath = Join-Path $tempDir "uncad-cad-$runId.dwg"
$previousResult = $env:UNCAD_CAD_INTEGRATION_RESULT
try {
    Copy-Item -LiteralPath $InputDrawing -Destination $drawingPath
    $commands = @(
        '_.NETLOAD',
        ('"' + ($assembly -replace '\\', '/') + '"'),
        $Command,
        '_.QUIT',
        '_Y'
    )
    Set-Content -LiteralPath $scriptPath -Value $commands -Encoding ASCII
    $env:UNCAD_CAD_INTEGRATION_RESULT = $resultPath
    $consoleOutput = (& $console /i $drawingPath /s $scriptPath 2>&1) -join [Environment]::NewLine
    $consoleExitCode = $LASTEXITCODE
    $resultExists = Test-Path -LiteralPath $resultPath
    $resultValue = if ($resultExists) {
        (Get-Content -LiteralPath $resultPath -Raw).Trim()
    } else {
        ''
    }
    if (!$resultExists -or $resultValue -ne 'PASS') {
        throw "AutoCAD integration test $Command did not report PASS (exit code $consoleExitCode).`n$resultValue`n$consoleOutput"
    }
    Write-Host 'UNCAD AutoCAD integration: PASS'
}
finally {
    $env:UNCAD_CAD_INTEGRATION_RESULT = $previousResult
    Remove-Item -LiteralPath $scriptPath -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $resultPath -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $drawingPath -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath ([IO.Path]::ChangeExtension($drawingPath, '.bak')) -ErrorAction SilentlyContinue
}
