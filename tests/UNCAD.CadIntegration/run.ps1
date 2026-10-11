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
$consoleLog = Join-Path $tempDir "uncad-cad-$runId.console.txt"
$consoleExitCode = -1
$consoleOutput = ''
$previousResult = $env:UNCAD_CAD_INTEGRATION_RESULT
try {
    Copy-Item -LiteralPath $InputDrawing -Destination $drawingPath
    $commands = @(
        # 必须先关掉 SECURELOAD：否则 AutoCAD 会从"不受信任的位置"加载 DLL 时
        # 只提示一句就放弃注册命令，脚本里只看到"未知命令 XXX"，非常难查。
        'SECURELOAD',
        '0',
        '_.NETLOAD',
        ('"' + ($assembly -replace '\\', '/') + '"'),
        $Command,
        '_.QUIT',
        '_Y'
    )
    Set-Content -LiteralPath $scriptPath -Value $commands -Encoding ASCII
    $env:UNCAD_CAD_INTEGRATION_RESULT = $resultPath
    # 用 cmd 重定向控制台输出：让 accoreconsole 的 stdin 保持原样。
    # 直接用 PowerShell 捕获 (& ... 2>&1) 会让脚本与命令行输入错位，
    # 表现是"SECURELOAD 之后紧接着 未知命令 0"。
    $nativePreference = $PSNativeCommandUseErrorActionPreference
    $PSNativeCommandUseErrorActionPreference = $false
    cmd /c "`"$console`" /i `"$drawingPath`" /s `"$scriptPath`" > `"$consoleLog`" 2>&1"
    $consoleExitCode = $LASTEXITCODE
    $PSNativeCommandUseErrorActionPreference = $nativePreference
    $consoleOutput = if (Test-Path -LiteralPath $consoleLog) {
        $bytes = [System.IO.File]::ReadAllBytes($consoleLog)
        $encoding = if ($bytes.Length -gt 1 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xFE) {
            [System.Text.Encoding]::Unicode
        } else {
            [System.Text.Encoding]::Default
        }
        $encoding.GetString($bytes)
    } else {
        ''
    }
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
    Remove-Item -LiteralPath $consoleLog -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $drawingPath -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath ([IO.Path]::ChangeExtension($drawingPath, '.bak')) -ErrorAction SilentlyContinue
}
