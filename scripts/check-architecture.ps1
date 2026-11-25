# Architecture report (report-only by default).
# Scans using directives and prints layer / cross-feature violations.
# Exit 0 unless -FailOnViolation is supplied.
param(
    [switch]$FailOnViolation
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root "src\UNCAD"
$violations = New-Object System.Collections.Generic.List[string]

function Get-CsFiles([string]$path) {
    if (-not (Test-Path -LiteralPath $path -PathType Container)) { return @() }
    return @(Get-ChildItem -LiteralPath $path -Filter *.cs -File -Recurse |
        Where-Object { $_.FullName -notmatch "\\(bin|obj)\\" } |
        ForEach-Object { $_.FullName })
}

function Add-Violation([string]$file, [string]$message) {
    $relative = $file.Substring($root.Length).TrimStart('\', '/')
    $script:violations.Add("$relative : $message")
}

function Get-FeatureNamespace([string]$file) {
    $marker = "Features" + [IO.Path]::DirectorySeparatorChar
    $index = $file.IndexOf($marker)
    if ($index -lt 0) { return "" }
    $rest = $file.Substring($index + $marker.Length)
    $slash = $rest.IndexOf([IO.Path]::DirectorySeparatorChar)
    if ($slash -lt 0) { return "" }
    return $rest.Substring(0, $slash)
}

# 1. Features must not reference another Feature (Features.Shared is allowed).
foreach ($file in Get-CsFiles (Join-Path $src "Features")) {
    $own = Get-FeatureNamespace $file
    foreach ($line in [IO.File]::ReadAllLines($file)) {
        $match = [regex]::Match($line.Trim(),
            '^using\s+UNCAD\.Features\.([A-Za-z0-9_]+)\s*;')
        if (-not $match.Success) { continue }
        $target = $match.Groups[1].Value
        if ($target -eq $own -or $target -eq "Shared") { continue }
        Add-Violation $file "Features.$own -> Features.$target"
    }
}

# 2. UI must not reference Features.
foreach ($file in Get-CsFiles (Join-Path $src "UI")) {
    foreach ($line in [IO.File]::ReadAllLines($file)) {
        if ($line.Trim().StartsWith("using UNCAD.Features", [StringComparison]::Ordinal)) {
            Add-Violation $file "UI -> Features"
        }
    }
}

# 3. Cad must not reference Features/UI/Core.Excel/NPOI/WinForms/WPF.
foreach ($file in Get-CsFiles (Join-Path $src "Cad")) {
    foreach ($line in [IO.File]::ReadAllLines($file)) {
        $trimmed = $line.Trim()
        foreach ($prefix in @("using UNCAD.Features", "using UNCAD.UI",
                "using UNCAD.Core.Excel", "using NPOI", "using System.Windows.Forms",
                "using System.Windows")) {
            if ($trimmed.StartsWith($prefix, [StringComparison]::Ordinal)) {
                Add-Violation $file "Cad -> $prefix"
            }
        }
    }
}

# 4. Infra must not reference Cad/Features/UI.
foreach ($file in Get-CsFiles (Join-Path $src "Infra")) {
    foreach ($line in [IO.File]::ReadAllLines($file)) {
        $trimmed = $line.Trim()
        foreach ($prefix in @("using UNCAD.Cad", "using UNCAD.Features", "using UNCAD.UI")) {
            if ($trimmed.StartsWith($prefix, [StringComparison]::Ordinal)) {
                Add-Violation $file "Infra -> $prefix"
            }
        }
    }
}

# 5. Core must stay free of AutoCAD/WinForms/WPF and upper layers.
foreach ($file in Get-CsFiles (Join-Path $src "Core")) {
    foreach ($line in [IO.File]::ReadAllLines($file)) {
        $trimmed = $line.Trim()
        foreach ($prefix in @("using Autodesk", "using System.Windows.Forms",
                "using System.Windows", "using UNCAD.Cad", "using UNCAD.Infra",
                "using UNCAD.Features", "using UNCAD.UI")) {
            if ($trimmed.StartsWith($prefix, [StringComparison]::Ordinal)) {
                Add-Violation $file "Core -> $prefix"
            }
        }
    }
}

Write-Host ("Architecture report: {0} violation(s)." -f $violations.Count)
foreach ($item in $violations) { Write-Host "  $item" }

if ($FailOnViolation -and $violations.Count -gt 0) { exit 1 }
exit 0
