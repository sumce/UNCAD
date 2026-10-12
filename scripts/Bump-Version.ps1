<#
.SYNOPSIS
按 D-032 调整版本号：加功能动 X.Y.Z，不加功能只动第四位。

.DESCRIPTION
- Kind=Patch   ：保持 VersionLabel（X.Y.Z）不变，只把第四位 +1，
                例如 2.6.9.0 -> 2.6.9.1。不新建 tag / RELEASE 文档。
- Kind=Feature ：把 VersionLabel 换成 -Label X.Y.Z，第四位归 0，
                需要新 tag、新 RELEASE 文档、VersionChangeLog 条目与版本号文档同步。

.EXAMPLE
.\scripts\Bump-Version.ps1 -Kind Patch
.\scripts\Bump-Version.ps1 -Kind Feature -Label 2.7.0
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet("Patch", "Feature")][string]$Kind,
    [string]$Label
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$csprojPath = Join-Path $root "src\UNCAD\UNCAD.csproj"
$metadataPath = Join-Path $root "src\UNCAD\Infra\ProductMetadata.cs"
$manifestPath = Join-Path $root "bundle\UNCAD.bundle\PackageContents.xml"
$configTestsPath = Join-Path $root "tests\UNCAD.Tests\ConfigKeysTests.cs"

function Read-Text([string]$Path) { return [IO.File]::ReadAllText($Path) }

function Write-Text([string]$Path, [string]$Text) {
    # 保留原文件的 BOM 状态：C# / ps1 / xml 缺 BOM 会被按系统代码页读，中文会乱。
    $bytes = [IO.File]::ReadAllBytes($Path)
    $hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    [IO.File]::WriteAllText($Path, $Text, (New-Object Text.UTF8Encoding($hasBom)))
}

function Replace-Once([string]$Path, [string]$Pattern, [string]$Replacement, [string]$What) {
    $text = Read-Text $Path
    if ($text -notmatch $Pattern) { throw "$What 未匹配：$Path" }
    $updated = [regex]::Replace($text, $Pattern, $Replacement, 1)
    Write-Text $Path $updated
    Write-Host ("  {0} -> {1}" -f $What, $Replacement)
}

# 注意：PowerShell 变量不区分大小写，这里不能叫 $label，否则会覆盖参数 $Label。
$currentLabel = ([regex]::Match((Read-Text $metadataPath), 'VersionLabel\s*=\s*"([^"]+)"')).Groups[1].Value
if (-not $currentLabel) { throw "无法从 ProductMetadata.cs 读取 VersionLabel。" }
$current = ([regex]::Match((Read-Text $csprojPath), '<AssemblyVersion>([^<]+)</AssemblyVersion>')).Groups[1].Value
if (-not $current) { throw "无法从 UNCAD.csproj 读取 AssemblyVersion。" }
$parts = $current.Split('.')
if ($parts.Count -ne 4) { throw "AssemblyVersion 不是四段（$current）。" }

if ($Kind -eq "Patch") {
    $nextVersion = "{0}.{1}.{2}.{3}" -f $parts[0], $parts[1], $parts[2], ([int]$parts[3] + 1)
    $nextLabel = $currentLabel
} else {
    if ($Label -notmatch '^\d+\.\d+\.\d+$') { throw "Feature 需要 -Label X.Y.Z，例如 -Label 2.7.0。" }
    $nextLabel = $Label
    $nextVersion = "$nextLabel.0"
}

if ($nextVersion -eq $current -and $nextLabel -eq $currentLabel) { throw "版本没有变化（当前 $current / $currentLabel）。" }

Write-Host ("版本：{0}（label {1}） -> {2}（label {3}）" -f $current, $currentLabel, $nextVersion, $nextLabel) -ForegroundColor Cyan

Replace-Once $csprojPath '<AssemblyVersion>[^<]+</AssemblyVersion>' ("<AssemblyVersion>" + $nextVersion + "</AssemblyVersion>") "csproj AssemblyVersion"
Replace-Once $csprojPath '<FileVersion>[^<]+</FileVersion>' ("<FileVersion>" + $nextVersion + "</FileVersion>") "csproj FileVersion"
Replace-Once $manifestPath 'AppVersion="[^"]+"' ('AppVersion="' + $nextVersion + '"') "PackageContents AppVersion"

if ($Kind -eq "Feature") {
    $today = (Get-Date).ToUniversalTime().ToString("yyyy-MM-dd")
    Replace-Once $csprojPath '<Version>[^<]+</Version>' ("<Version>" + $nextLabel + "</Version>") "csproj Version"
    Replace-Once $metadataPath 'VersionLabel\s*=\s*"[^"]+"' ('VersionLabel = "' + $nextLabel + '"') "ProductMetadata VersionLabel"
    Replace-Once $metadataPath 'ReleaseDateUtc\s*=\s*"[^"]+"' ('ReleaseDateUtc = "' + $today + '"') "ProductMetadata ReleaseDateUtc"
    Replace-Once $configTestsPath ('Assert\.Equal\("' + [regex]::Escape($currentLabel) + '", info\.Version\);') ('Assert.Equal("' + $nextLabel + '", info.Version);') "ConfigKeysTests 期望值"

    $releaseNote = Join-Path $root ("docs\RELEASE-" + $nextLabel + ".md")
    if (-not (Test-Path -LiteralPath $releaseNote)) {
        $lines = @(
            "# UNCAD Pro $nextLabel",
            "",
            "发布日期：$today",
            "",
            "## 新增",
            "",
            "（写清楚新增了什么、影响哪些命令、明确不做什么。）",
            "",
            "## 验证",
            "",
            "- dotnet test UNCAD.slnx：全部通过。"
        )
        [IO.File]::WriteAllLines($releaseNote, $lines, (New-Object Text.UTF8Encoding($true)))
        Write-Host "  新建发布说明 docs\RELEASE-$nextLabel.md（请补内容）" -ForegroundColor Yellow
    }
    Write-Host ""
    Write-Host "加功能（D-032）还要做：" -ForegroundColor Yellow
    Write-Host "  1. src\UNCAD\Infra\VersionChangeLog.cs 增加 $nextLabel 条目"
    Write-Host "  2. README.md / docs\PROJECT_CONTEXT.md / AGENTS.md 的版本号与要点"
    Write-Host "  3. .\release.ps1 -NoRestore 出包，然后 git tag -a v$nextLabel"
} else {
    Write-Host ""
    Write-Host "不加功能（D-032）：不要新开 tag，也不要新建 RELEASE 文档；" -ForegroundColor Yellow
    Write-Host "复用 docs\RELEASE-$currentLabel.md，出包后直接推送即可。"
}
