param(
    [string]$AutoCADDir = 'D:\Program Files\Autodesk\AutoCAD 2022',
    [string]$InputDrawing,
    [string]$Command = 'UNCAD_COLOR_SELFTEST'
)
$ErrorActionPreference = 'Stop'
$projectDir = $PSScriptRoot
$repoRoot = (Resolve-Path (Join-Path $projectDir '..\..')).Path
if ([string]::IsNullOrWhiteSpace($InputDrawing)) {
    # 表格填充自检的夹具会"从当前图纸里取那张 13×6 的 BOQ 表"当模板用：
    # 它必须在出厂图框模板上跑。默认的 UNCAD.dwg 是真实客户图纸，里面也可能有
    # 13×6 的表，于是会走出误导性的"生成行高不对"报错（见 RELEASE-2.6.4）。
    # 注意：PowerShell 5.1 不支持 "$x = if (...) {...} else {...}" 表达式写法。
    if ($Command -eq 'UNCAD_TABLE_FILL_SELFTEST') {
        $InputDrawing = Join-Path $repoRoot 'src\UNCAD\bin\Release\net48\Resources\XFrameTemplate.dwg'
    } else {
        $InputDrawing = Join-Path $repoRoot 'UNCAD.dwg'
    }
}
$console = Join-Path $AutoCADDir 'accoreconsole.exe'
$project = Join-Path $projectDir 'UNCAD.CadIntegration.csproj'
$assembly = Join-Path $projectDir 'bin\Release\net48\UNCAD.CadIntegration.dll'
if (!(Test-Path -LiteralPath $console)) { throw "AutoCAD Core Console not found: $console" }

dotnet build $project -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'CAD integration fixture build failed.' }
# 图纸存在性放在构建之后：图框模板来自 Release 构建输出，首次运行才拿得到。
if (!(Test-Path -LiteralPath $InputDrawing)) { throw "Input drawing not found: $InputDrawing" }

$runId = [Guid]::NewGuid().ToString('N')
$tempDir = [System.IO.Path]::GetTempPath()
$scriptPath = Join-Path $tempDir "uncad-cad-$runId.scr"
$resultPath = Join-Path $tempDir "uncad-cad-$runId.result"
$drawingPath = Join-Path $tempDir "uncad-cad-$runId.dwg"
$consoleLog = Join-Path $tempDir "uncad-cad-$runId.console.txt"
$consoleExitCode = -1
$consoleOutput = ''
$previousResult = $env:UNCAD_CAD_INTEGRATION_RESULT
$succeeded = $false
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
    $succeeded = $true
    Write-Host 'UNCAD AutoCAD integration: PASS'
}
finally {
    $env:UNCAD_CAD_INTEGRATION_RESULT = $previousResult
    # 失败时保留现场：以前控制台日志与结果文件在 finally 里被删掉，调用方只看到
    # "没有报 PASS"，真正的原生报错（例如 accoreconsole 崩溃）无从查起。
    if (!$succeeded) {
        try {
            $keepDir = Join-Path $repoRoot 'tmp'
            New-Item -ItemType Directory -Path $keepDir -Force | Out-Null
            $keep = Join-Path $keepDir ($Command + '-' + $runId + '.cadlog.txt')
            if (Test-Path -LiteralPath $consoleLog) {
                Copy-Item -LiteralPath $consoleLog -Destination $keep -Force
            }
            Write-Host ('CAD 自检失败现场已保留：' + $keep)
            Write-Host ('结果文件内容：[' + $resultValue + ']')
        } catch { }
    }
    Remove-Item -LiteralPath $scriptPath -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $resultPath -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $consoleLog -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $drawingPath -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath ([IO.Path]::ChangeExtension($drawingPath, '.bak')) -ErrorAction SilentlyContinue
}
