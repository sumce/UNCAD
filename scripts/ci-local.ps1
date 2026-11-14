# =====================================================================
# scripts/ci-local.ps1 —— 本地 CI 兜底(UNCAD 3.1.0 加固版)
#
# 用途:
#   运行 dotnet test UNCAD.slnx,解析控制台汇总的通过/失败/跳过/总数;
#   再按 tests\UNCAD.Tests\tests.runsettings:9-14 注释的同一口径动态扫描
#   tests\UNCAD.Tests\**\*.cs,统计"源码期望数"([Fact] 出现次数 + 含
#   [InlineData 的行数),并强制门禁:执行数 != 期望数 也算失败。
#   背景:xunit 在宿主加载不了 x64 AcDbMgd 等情况下会把整个测试程序集
#   静默跳过且 dotnet test 仍返回退出码 0,只看退出码会假绿——这正是
#   tests.runsettings 里 DisableAppDomain 防的那类事故,本脚本是第二道闸。
#   仓库正在持续新增测试文件,期望数必须每次动态扫描源码,不维护清单。
#
# 用法:
#   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\ci-local.ps1
#   .\scripts\ci-local.ps1 -NoRestore                        # 透传 --no-restore
#   .\scripts\ci-local.ps1 -Filter "FullyQualifiedName~Fill" # 透传 --filter;
#     过滤运行只跑子集,期望数门禁自动关闭(仅看退出码与汇总行)
#
# 日志:
#   完整控制台输出逐行回显并落盘 tmp\ci\ci-YYYYMMDD-HHmmss.log(UTF-8);
#   tmp/ 已在 .gitignore,目录不存在时脚本自建。
#
# 并发保护(命中即退出码 2,提示稍后再试;避免与并行构建/发布抢 obj/bin):
#   a) scripts\build-lock.ps1 持有的 tmp\build.lock 被独占占用;
#   b) 已有命令行在跑 test/build/vstest/msbuild 的 dotnet 进程,或 testhost 进程
#      (MSBuild 常驻节点与 build-server 构建后仍存活,不算"正在构建");
#   c) 另一个 ci-local 实例持有 tmp\ci\ci.lock(原子 CreateNew 建锁并记录
#      持锁 PID;持锁进程已退出则视为陈锁自动清理后重取)。
#
# 退出码:
#   0 = 全部通过,且(未带 -Filter 时)执行数 == 源码期望数
#   1 = 测试失败 / dotnet test 非零退出 / 无汇总行 / 执行数 != 期望数
#   2 = 并发保护触发
#   3 = 环境错误(dotnet、UNCAD.slnx、测试目录、日志目录、gitignore 状态)
# =====================================================================
[CmdletBinding()]
param(
    # 透传 dotnet test 的 --no-restore(本机已还原过时跳过还原,加快启动)
    [switch]$NoRestore,
    # 透传 dotnet test 的 --filter,如 "FullyQualifiedName~Fill"
    [string]$Filter = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exitOk = 0
$exitTest = 1
$exitBusy = 2
$exitEnv = 3
$exitCode = $exitTest

# ---------- 0. 环境自检(退出码 3) ----------
$slnx = Join-Path $root "UNCAD.slnx"
$testRoot = Join-Path $root "tests\UNCAD.Tests"
if (-not (Test-Path -LiteralPath $slnx -PathType Leaf)) {
    Write-Host "环境错误:找不到 $slnx" -ForegroundColor Red
    exit $exitEnv
}
if (-not (Test-Path -LiteralPath $testRoot -PathType Container)) {
    Write-Host "环境错误:找不到测试目录 $testRoot" -ForegroundColor Red
    exit $exitEnv
}
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Write-Host "环境错误:PATH 里没有 dotnet" -ForegroundColor Red
    exit $exitEnv
}
$ciDir = Join-Path $root "tmp\ci"
try {
    New-Item -ItemType Directory -Force -Path $ciDir | Out-Null
} catch {
    Write-Host "环境错误:无法创建日志目录 $ciDir($($_.Exception.Message))" -ForegroundColor Red
    exit $exitEnv
}
# tmp/ 必须保持被 .gitignore 忽略,否则 CI 日志会污染可提交工作区(AGENTS.md:tmp 不入库)。
if (Get-Command git -ErrorAction SilentlyContinue) {
    $ignored = $true
    try {
        & git -C $root check-ignore -q -- "tmp/ci"
        if ($LASTEXITCODE -ne 0) { $ignored = $false }
    } catch { $ignored = $true } # git 异常时不拦路,依赖仓库现状
    if (-not $ignored) {
        Write-Host "环境错误:tmp/ci 未被 .gitignore 忽略,拒绝把 CI 日志写进可提交区域。" -ForegroundColor Red
        exit $exitEnv
    }
}

# ---------- 1. 并发保护(退出码 2) ----------
# (a) scripts\build-lock.ps1 在构建/测试期间以独占句柄持有 tmp\build.lock;
#     文件锁随进程退出自动释放,不存在陈锁问题。
$buildLock = Join-Path $root "tmp\build.lock"
if (Test-Path -LiteralPath $buildLock -PathType Leaf) {
    $probe = $null
    try { $probe = [IO.File]::Open($buildLock, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None) } catch { }
    if ($null -eq $probe) {
        Write-Host "并发保护:tmp\build.lock 被占用(有 scripts\build-lock.ps1 构建/测试在跑),请稍后再试。" -ForegroundColor Yellow
        exit $exitBusy
    }
    $probe.Dispose()
}

# (b) 命令行里正在执行 test/build/vstest/msbuild 的 dotnet 进程,以及 testhost。
#     MSBuild 常驻节点(/nodemode)与 build-server 在构建结束后仍存活,
#     若算作"正在构建"会让本脚本永远误报忙,故显式排除。
function Find-BusyBuildProcess {
    foreach ($proc in Get-CimInstance -ClassName Win32_Process -Filter "Name = 'dotnet.exe'" -ErrorAction SilentlyContinue) {
        $cmd = " " + ([string]$proc.CommandLine).ToLowerInvariant() + " "
        if ($cmd -match "msbuild-server|build-server|/nodemode") { continue }
        if ($cmd -match " test | build | vstest| msbuild|testhost") { return $proc }
    }
    return $null
}
$busy = Find-BusyBuildProcess
if ($null -ne $busy) {
    Write-Host ("并发保护:dotnet 进程 PID {0} 正在跑构建/测试:{1}" -f $busy.ProcessId, $busy.CommandLine) -ForegroundColor Yellow
    Write-Host "请等它结束后再运行本脚本。" -ForegroundColor Yellow
    exit $exitBusy
}
if (Get-Process -Name "testhost*" -ErrorAction SilentlyContinue) {
    Write-Host "并发保护:已有 testhost 进程在跑(测试宿主未退出),请稍后再试。" -ForegroundColor Yellow
    exit $exitBusy
}

# (c) 本脚本实例互斥:两个 ci-local 同时通过上面的进程扫描(彼此都还没起
#     dotnet)同样会抢 obj/bin,用 tmp\ci\ci.lock 的原子 CreateNew 关掉该窗口。
$ciLock = Join-Path $ciDir "ci.lock"
if (Test-Path -LiteralPath $ciLock -PathType Leaf) {
    $stale = $true
    $holderPid = 0
    try {
        $holderPid = [int]((Get-Content -LiteralPath $ciLock -Raw).Trim())
        if (Get-Process -Id $holderPid -ErrorAction SilentlyContinue) { $stale = $false }
    } catch { }
    if (-not $stale) {
        Write-Host "并发保护:另一个 ci-local 实例(PID $holderPid)正持有 $ciLock,请稍后再试。" -ForegroundColor Yellow
        exit $exitBusy
    }
    # 持锁进程已不在 → 上次异常退出留下的陈锁,清掉重取(宁误报不漏报的
    # 简单启发:PID 存活即认为锁仍有效)。
    Remove-Item -LiteralPath $ciLock -Force
}
try {
    $lockStream = [IO.File]::Open($ciLock, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try {
        $pidBytes = [Text.Encoding]::UTF8.GetBytes("$PID")
        $lockStream.Write($pidBytes, 0, $pidBytes.Length)
    } finally { $lockStream.Dispose() }
} catch {
    Write-Host "并发保护:获取 $ciLock 失败($($_.Exception.Message)),请稍后再试。" -ForegroundColor Yellow
    exit $exitBusy
}
$ownCiLock = $true

# ---------- 以下进入主 try:任何失败路径都会经 finally 落日志、释放锁 ----------
$factCount = 0
$inlineCount = 0
$expectedCount = 0
$testArgs = @()
$console = New-Object System.Collections.Generic.List[string]
$testExit = -1
$logPath = Join-Path $ciDir ("ci-" + (Get-Date -Format "yyyyMMdd-HHmmss") + ".log")

try {
    # ---------- 2. 源码期望数(与 tests.runsettings:9-14 注释同口径) ----------
    # 动态扫描:新增/删除测试文件无需改本脚本。[Fact] 按"出现次数"计,
    # [InlineData] 按"出现行数"计;bin/obj 下的生成代码不计。
    $testFiles = @(Get-ChildItem -LiteralPath $testRoot -Filter *.cs -File -Recurse |
        Where-Object { $_.FullName -notmatch "\\(bin|obj)\\" })
    foreach ($file in $testFiles) {
        $text = [IO.File]::ReadAllText($file.FullName)
        $factCount += [regex]::Matches($text, "\[Fact\]").Count
        foreach ($line in ($text -split "\r?\n")) {
            if ($line -match "\[InlineData") { $inlineCount++ }
        }
    }
    $expectedCount = $factCount + $inlineCount
    Write-Host ("源码期望数: {0} = [Fact] {1} 处 + [InlineData] 行 {2}(扫描 {3} 个测试源文件)" -f `
        $expectedCount, $factCount, $inlineCount, $testFiles.Count) -ForegroundColor Cyan

    # ---------- 3. 运行 dotnet test,逐行回显并缓存(tee) ----------
    $testArgs = @("test", $slnx, "--nologo")
    if ($NoRestore) { $testArgs += "--no-restore" }
    if (-not [string]::IsNullOrWhiteSpace($Filter)) { $testArgs += @("--filter", $Filter) }
    Write-Host ("运行: dotnet " + ($testArgs -join " ")) -ForegroundColor Cyan
    # PS5.1:EAP=Stop 时 2>&1 会把 dotnet 写 stderr 的告警升级成终止错误,
    # 捕获期间临时放宽;成败仍以 $LASTEXITCODE 为准。
    $previousEap = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    # vstest 汇总行随系统区域本地化(中文系统输出"已通过! - 失败: 0，通过: …"全角逗号),
    # 强制子进程英文保证下方正则可靠命中;正则本身同时兼容中英文双格式兜底。
    $previousLang = $env:DOTNET_CLI_UI_LANGUAGE
    $env:DOTNET_CLI_UI_LANGUAGE = "en"
    try {
        Push-Location $root
        try {
            & dotnet @testArgs 2>&1 | ForEach-Object {
                $line = [string]$_
                [void]$console.Add($line)
                Write-Host $line
            }
            $testExit = $LASTEXITCODE
        } finally { Pop-Location }
    } finally {
        $ErrorActionPreference = $previousEap
        if ($null -eq $previousLang) { Remove-Item Env:\DOTNET_CLI_UI_LANGUAGE -ErrorAction SilentlyContinue }
        else { $env:DOTNET_CLI_UI_LANGUAGE = $previousLang }
    }

    # ---------- 4. 解析汇总行并执行门禁 ----------
    # xunit(vstest 适配器)的汇总行格式(标签随区域本地化,数字右对齐补空格):
    #   英文: Passed!  - Failed:     0, Passed:  1428, Skipped:     0, Total:     1428, Duration: ...
    #   中文: 已通过! - 失败:     0，通过:  1428，已跳过:     0，总计:     1428，持续时间 ...
    # 因此匹配"叹号+短横线后连续四组 标签:数字"(分隔逗号兼容半/全角),
    # 不锁标签文案;解决方案含多个项目时可能有多行汇总,逐行累加。
    $summaryPattern = "(?:Passed|Failed|已通过|失败)!+\s*[-－—]\s*[^:：!]{1,12}[:：]\s*(\d+)\s*[,，]\s*[^:：!]{1,12}[:：]\s*(\d+)\s*[,，]\s*[^:：!]{1,12}[:：]\s*(\d+)\s*[,，]\s*[^:：!]{1,12}[:：]\s*(\d+)"
    $failedCount = 0
    $passedCount = 0
    $skippedCount = 0
    $totalCount = 0
    $summaryLines = 0
    foreach ($line in $console) {
        $m = [regex]::Match($line, $summaryPattern)
        if (-not $m.Success) { continue }
        $summaryLines++
        $failedCount += [int]$m.Groups[1].Value
        $passedCount += [int]$m.Groups[2].Value
        $skippedCount += [int]$m.Groups[3].Value
        $totalCount += [int]$m.Groups[4].Value
    }
    $executedCount = $passedCount + $failedCount # Skipped 视为未执行

    if ($testExit -ne 0) {
        $exitCode = $exitTest
        Write-Host ("结果:失败 —— dotnet test 退出码 {0}。" -f $testExit) -ForegroundColor Red
    } elseif ($summaryLines -eq 0) {
        $exitCode = $exitTest
        Write-Host "结果:失败 —— 输出中没有 xunit 汇总行(Passed!/Failed! - Failed: ... Total: ...)。" -ForegroundColor Red
        Write-Host "宿主没把测试程序集跑起来时就是这种情况(tests.runsettings:9-14 防的静默跳过),这不是通过。" -ForegroundColor Red
    } elseif ($failedCount -gt 0) {
        $exitCode = $exitTest
        Write-Host ("结果:失败 —— {0} 项失败(通过 {1},跳过 {2},汇总总数 {3})。" -f `
            $failedCount, $passedCount, $skippedCount, $totalCount) -ForegroundColor Red
    } elseif (-not [string]::IsNullOrWhiteSpace($Filter)) {
        $exitCode = $exitOk
        Write-Host ("结果:通过(过滤运行,期望数门禁关闭)—— 执行 {0} 项全过;源码期望数 {1} 仅供比对。" -f `
            $executedCount, $expectedCount) -ForegroundColor Green
    } elseif ($executedCount -ne $expectedCount) {
        $exitCode = $exitTest
        Write-Host ("结果:失败 —— 执行数 {0} != 源码期望数 {1}(另报告跳过 {2},汇总总数 {3})。" -f `
            $executedCount, $expectedCount, $skippedCount, $totalCount) -ForegroundColor Red
        Write-Host "xunit 加载不了 x64 AcDbMgd 等情况下会整程序集静默跳过且退出码为 0(tests.runsettings:9-14),此门禁专门拦这种假绿。" -ForegroundColor Red
    } else {
        $exitCode = $exitOk
        Write-Host ("结果:通过 —— 执行 {0}/{1} 项,与源码期望数一致([Fact] {2} 处 + [InlineData] 行 {3})。" -f `
            $executedCount, $expectedCount, $factCount, $inlineCount) -ForegroundColor Green
    }
} finally {
    # ---------- 5. 落盘日志 + 释放锁(任何退出路径都走到这里) ----------
    try {
        $logLines = @(
            ("=" * 72),
            ("UNCAD ci-local 日志  " + (Get-Date -Format "yyyy-MM-dd HH:mm:ss")),
            ("命令: dotnet " + ($testArgs -join " ")),
            ("源码期望数: $expectedCount = [Fact] $factCount 处 + [InlineData] 行 $inlineCount"),
            ("dotnet 退出码: $testExit / 脚本退出码: $exitCode"),
            ("=" * 72)
        ) + $console.ToArray() + @("脚本退出码: $exitCode")
        Set-Content -LiteralPath $logPath -Value $logLines -Encoding UTF8
        Write-Host ("日志: " + $logPath) -ForegroundColor Cyan
    } catch {
        Write-Host ("警告:日志落盘失败({0})" -f $_.Exception.Message) -ForegroundColor Yellow
    }
    if ($ownCiLock) { Remove-Item -LiteralPath $ciLock -Force -ErrorAction SilentlyContinue }
}

exit $exitCode
