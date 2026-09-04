[CmdletBinding()]
param(
    [ValidateSet("All", "Fill", "QuickLine", "Layout", "Stats", "Settings", "Submit")]
    [string]$Area = "All",
    [switch]$IncludeTests,
    [string]$OutputFile = ""
)

$ErrorActionPreference = "Stop"
$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$commonFiles = @(
    "AGENTS.md",
    "docs\PROJECT_CONTEXT.md",
    "docs\DECISIONS.md",
    "docs\KNOWN-ISSUES.md",
    "docs\DEVELOPMENT.md"
)
$files = New-Object System.Collections.Generic.List[string]

function Add-RelativeFile {
    param([string]$RelativePath)
    $full = Join-Path $root $RelativePath
    if (Test-Path -LiteralPath $full -PathType Leaf) {
        if (-not $files.Contains($RelativePath)) { [void]$files.Add($RelativePath) }
    }
}

function Add-SourceDirectory {
    param([string]$RelativePath)
    $full = Join-Path $root $RelativePath
    if (Test-Path -LiteralPath $full -PathType Container) {
        Get-ChildItem -LiteralPath $full -Filter *.cs -File -Recurse |
            Sort-Object FullName |
            ForEach-Object {
                $relative = $_.FullName.Substring($root.Length).TrimStart('\', '/')
                Add-RelativeFile $relative
            }
    }
}

function Add-TestMatches {
    param([string[]]$Patterns)
    $testRoot = Join-Path $root "tests\UNCAD.Tests"
    foreach ($pattern in $Patterns) {
        Get-ChildItem -LiteralPath $testRoot -Filter $pattern -File -ErrorAction SilentlyContinue |
            Sort-Object Name |
            ForEach-Object {
                $relative = $_.FullName.Substring($root.Length).TrimStart('\', '/')
                Add-RelativeFile $relative
            }
    }
}

$commonFiles | ForEach-Object { Add-RelativeFile $_ }
Add-RelativeFile "src\UNCAD\Infra\CommandIds.cs"
Add-RelativeFile "src\UNCAD\Infra\CommandHelpCatalog.cs"

switch ($Area) {
    "All" {
        Add-SourceDirectory "src\UNCAD\Core"
        Add-SourceDirectory "src\UNCAD\Cad"
        Add-SourceDirectory "src\UNCAD\Features"
        Add-SourceDirectory "src\UNCAD\Infra"
        Add-SourceDirectory "src\UNCAD\UI"
    }
    "Fill" {
        Add-SourceDirectory "src\UNCAD\Features\Fill"
        Add-SourceDirectory "src\UNCAD\Core\Fill"
        Add-SourceDirectory "src\UNCAD\Core\Excel"
        Add-RelativeFile "src\UNCAD\Infra\ConfigKeys.cs"
        Add-RelativeFile "src\UNCAD\Infra\FillSettings.cs"
        Add-RelativeFile "src\UNCAD\Infra\FillColorSettings.cs"
        Add-RelativeFile "src\UNCAD\UI\FillReviewForm.cs"
        Add-RelativeFile "src\UNCAD\UI\FillUpdateCompareForm.cs"
        Add-RelativeFile "src\UNCAD\UI\BatchFillConfirmationForm.cs"
    }
    "QuickLine" {
        Add-SourceDirectory "src\UNCAD\Features\Unl"
        Add-SourceDirectory "src\UNCAD\Core\QuickLine"
        Add-SourceDirectory "src\UNCAD\Cad\QuickLine"
        Add-RelativeFile "src\UNCAD\UI\UiTheme.cs"
    }
    "Layout" {
        Add-SourceDirectory "src\UNCAD\Features\XLayout"
        Add-SourceDirectory "src\UNCAD\Core\Dwg"
        Add-SourceDirectory "src\UNCAD\Core\Geometry"
        Add-RelativeFile "src\UNCAD\Cad\FrameRegionCollector.cs"
        Add-RelativeFile "src\UNCAD\UI\XLayoutSummaryForm.cs"
        Add-RelativeFile "src\UNCAD\UI\XmergeForm.cs"
    }
    "Stats" {
        Add-SourceDirectory "src\UNCAD\Features\Stat"
        Add-SourceDirectory "src\UNCAD\Features\Unadd"
        Add-SourceDirectory "src\UNCAD\Core\Stat"
        Add-SourceDirectory "src\UNCAD\Core\Text"
        Add-RelativeFile "src\UNCAD\UI\XstsForm.cs"
    }
    "Settings" {
        Add-SourceDirectory "src\UNCAD\Features\ConfigCenter"
        Add-RelativeFile "src\UNCAD\Infra\ConfigKeys.cs"
        Add-RelativeFile "src\UNCAD\Infra\FillSettings.cs"
        Add-RelativeFile "src\UNCAD\UI\UnifiedSettingsForm.cs"
    }
    "Submit" {
        Add-SourceDirectory "src\UNCAD\Features\Submit"
        Add-SourceDirectory "src\UNCAD\Core\Submission"
        Add-SourceDirectory "src\UNCAD\Core\IO"
    }
}

if ($IncludeTests) {
    switch ($Area) {
        "All" { Add-TestMatches @("*Tests.cs") }
        "Fill" { Add-TestMatches @("*Fill*Tests.cs", "*Boq*Tests.cs", "*Excel*Tests.cs", "*Frame*Tests.cs", "*Outlet*Tests.cs") }
        "QuickLine" { Add-TestMatches @("*QuickLine*Tests.cs") }
        "Layout" { Add-TestMatches @("*Layout*Tests.cs", "*Dwg*Tests.cs", "*Frame*Tests.cs") }
        "Stats" { Add-TestMatches @("*Stat*Tests.cs", "*Xsts*Tests.cs", "*Summation*Tests.cs") }
        "Settings" { Add-TestMatches @("*Config*Tests.cs", "*Settings*Tests.cs") }
        "Submit" { Add-TestMatches @("*Submission*Tests.cs", "*Submit*Tests.cs") }
    }
}

$branch = (& git -C $root branch --show-current 2>$null)
$status = @(& git -C $root status --short 2>$null)
$recent = @(& git -C $root log -5 --oneline 2>$null)
$lines = New-Object System.Collections.Generic.List[string]
[void]$lines.Add("# UNCAD context pack")
[void]$lines.Add("")
[void]$lines.Add("Area: $Area")
[void]$lines.Add("Branch: $branch")
[void]$lines.Add("Generated: $([DateTime]::Now.ToString('yyyy-MM-dd HH:mm:ss'))")
[void]$lines.Add("")
[void]$lines.Add("## Recent commits")
$recent | ForEach-Object { [void]$lines.Add("- $_") }
[void]$lines.Add("")
[void]$lines.Add("## Worktree")
if ($status.Count -eq 0) { [void]$lines.Add("- clean") }
else { $status | ForEach-Object { [void]$lines.Add("- ``$_``") } }
[void]$lines.Add("")
[void]$lines.Add("## Read these files")
$files | ForEach-Object { [void]$lines.Add("- ``$_``") }

$content = $lines -join [Environment]::NewLine
if ([string]::IsNullOrWhiteSpace($OutputFile)) {
    Write-Output $content
} else {
    $destination = if ([IO.Path]::IsPathRooted($OutputFile)) {
        $OutputFile
    } else {
        Join-Path $root $OutputFile
    }
    $parent = Split-Path -Parent $destination
    if ($parent -and -not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    Set-Content -LiteralPath $destination -Value $content -Encoding UTF8
    Write-Output "Context pack: $destination"
}
