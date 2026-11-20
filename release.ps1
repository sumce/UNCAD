[CmdletBinding()]
param(
    [string]$Suffix = "",
    [string]$Configuration = "Release",
    [switch]$NoBuild,
    [switch]$NoRestore,
    # 性能冒烟门禁默认执行；只有紧急发布才允许用 -SkipPerf 显式跳过。
    [switch]$SkipPerf,
    # 图框迁移自检门禁默认执行；只有紧急发布才允许用 -SkipMigration 显式跳过。
    [switch]$SkipMigration,
    # 开发版通道（D-039）：跳过"发布页 + git tag"两道正式版门禁，用于真机验收
    # 装机包；测试/性能/迁移门禁照常执行。正式发布禁止携带 -Dev。
    [switch]$Dev
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
    $sourceInputs = @(Get-ChildItem (Join-Path $root "src") -Recurse -File |
        Where-Object { $_.Extension -in @(".cs", ".csproj", ".tsv", ".svg", ".xlsx", ".dwg", ".html", ".css", ".js", ".txt") })
    $sourceInputs += @(Get-Item (Join-Path $root "BOQ_Template.xlsx"))
    # Scripts and manifests affect the package without touching UNCAD.dll;
    # their staleness must also reject a -NoBuild shortcut.
    $sourceInputs += @(Get-ChildItem $root -File |
        Where-Object { $_.Extension -in @(".ps1", ".xml") })
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
Assert-FileMatches (Join-Path (Split-Path -Parent $buildOutput) "UNCAD.Preview.exe") (Join-Path $bundle "UNCAD.Preview.exe") "WebV viewer"
Assert-FileMatches (Join-Path (Split-Path -Parent $buildOutput) "UNCAD.Preview.exe.config") (Join-Path $bundle "UNCAD.Preview.exe.config") "WebV configuration"
Assert-TreeMatches (Join-Path (Split-Path -Parent $buildOutput) "Web") (Join-Path $bundle "Web") "WebV assets"

$testArgs = @("test", "$root\UNCAD.slnx", "-c", $Configuration)
# build.ps1 already built the solution; avoid a second timestamped DLL build.
$testArgs += "--no-build"
if ($NoRestore) { $testArgs += "--no-restore" }
& dotnet $testArgs
if ($LASTEXITCODE -ne 0) { throw "Release tests failed." }

# 性能冒烟门禁（accoreconsole 真机）：在程序化生成的大图（36 图框 + 3600 线 +
# 统计文字）上，计时批量 U1U 的两条历史热点读路径——整图 FrameRegionCollector
# 收集与按图框统计文字求和。背景：2.9.0 的 O(N²) 全图扫描曾造成单帧 8.7 秒、
# 一天连发五个性能补丁；不设命令级基准，这类回归只能到客户机器上才暴露。
# 超预算时 run.ps1 会抛错终止发布，各阶段耗时明细见其输出与 %UNCAD_PERF_SMOKE_REPORT%。
if (-not $SkipPerf) {
    $perfDrawing = Join-Path (Split-Path -Parent $buildOutput) "Resources\XFrameTemplate.dwg"
    & (Join-Path $root "tests\UNCAD.CadIntegration\run.ps1") `
        -Command UNCAD_PERF_SMOKE -InputDrawing $perfDrawing
    if ($LASTEXITCODE -ne 0) {
        throw "Release performance smoke gate failed. 各阶段耗时明细见上方输出；修复后重试，紧急发布可用 -SkipPerf 跳过（须在发布记录中注明原因）。"
    }
}

# 图框迁移自检门禁（accoreconsole 真机）：在程序化构造的"老图"（四代图框
# frame/frame_20260812/xframe/xframe_v2 + 旧式内嵌 FRAMEINFO_JSON v1 扁平 JSON +
# BRIDGE_FRAME_INFO/CONDUIT_INFO + D-027 行高的 BOQ 表）上，按 U1F/U1U 首次写入的
# 同一迁移链断言：四代图框全部识别、旧内嵌 JSON 迁到独立 frameinfo_json 块且身份
# （机台ID/替代型号/历史）不丢、迁移不改用户表格行列尺寸、新定义没有的旧属性被移除。
# 背景：AGENTS.md 硬约束「Old frames and xframe frames remain readable. Migration
# must not resize a user's existing table, row, or column」此前只有单测字符串断言；
# 任何 XFrameMigrationService/FrameInfoJsonBlockWriter/模板 DWG 的回归都会在这里
# 被真机拦截。断言明细见 run.ps1 输出与 %UNCAD_MIGRATION_SELFTEST_REPORT%。
if (-not $SkipMigration) {
    $migrationDrawing = Join-Path (Split-Path -Parent $buildOutput) "Resources\XFrameTemplate.dwg"
    & (Join-Path $root "tests\UNCAD.CadIntegration\run.ps1") `
        -Command UNCAD_MIGRATION_SELFTEST -InputDrawing $migrationDrawing
    if ($LASTEXITCODE -ne 0) {
        throw "Release frame-migration self-test gate failed. 断言明细见上方输出与 %UNCAD_MIGRATION_SELFTEST_REPORT%；修复后重试，紧急发布可用 -SkipMigration 跳过（须在发布记录中注明原因）。"
    }
}

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
# D-032：前三段必须等于 VersionLabel；第四位是"同版本内的补丁号"，不加功能时递增。
if ($version -ne $versionLabel -and -not $version.StartsWith($versionLabel + ".")) {
    throw "Release version mismatch: ProductMetadata.VersionLabel='$versionLabel' does not agree with PackageContents.xml AppVersion='$version'."
}
# D-039：开发版不出正式发布页、不打 tag——tag 断档门禁只约束正式版；
# 验收装机包由 -Dev 出，正式版发布时禁止 -Dev（见下方 throw）。
if ($versionLabel -like "*-dev") {
    if (-not $Dev) {
        throw "VersionLabel '$versionLabel' is a development version; use -Dev for acceptance builds, or strip '-dev' for a stable release (D-039)."
    }
} elseif ($Dev) {
    throw "-Dev is only allowed for development versions (X.Y.Z-dev). Stable releases must not use -Dev (D-039)."
}
$releaseNote = Join-Path $root ("docs\RELEASE-" + $versionLabel + ".md")
if (-not $Dev) {
    if (-not (Test-Path $releaseNote -PathType Leaf)) {
        throw "Release note document is missing: docs\RELEASE-$versionLabel.md (see docs/RELEASE.md)."
    }
    if ([IO.File]::ReadAllText($releaseNote).Trim().Length -eq 0) {
        throw "Release note document is empty: docs\RELEASE-$versionLabel.md"
    }
}
# 标签门禁:发布的版本必须有一个指向本线的 tag。没有门禁时标签会静默断档
# (曾一直停在 v2.1.6)。要求 tag 是 HEAD 的祖先而不是 HEAD 本身,是为了允许
# 在发布提交之后再追加不改变产物的提交(例如文档或仓库卫生)。
$versionTag = "v" + $versionLabel
if (-not $Dev) {
    if (-not (git -C $root rev-parse -q --verify "refs/tags/$versionTag" 2>$null)) {
        throw "Release tag '$versionTag' does not exist. Tag the release commit first, for example: git tag -a $versionTag -m 'UNCAD Pro $versionLabel'"
    }
    & git -C $root merge-base --is-ancestor "$versionTag^{commit}" HEAD 2>$null
    if ($LASTEXITCODE -ne 0) {
        throw "Release tag '$versionTag' does not point at an ancestor of HEAD; it belongs to another line of history."
    }
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
