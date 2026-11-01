param(
    [ValidateSet('build','test','both')][string]$Run = 'build',
    [string]$Filter = '',
    [int]$TimeoutSeconds = 3600
)
# Serialize dotnet build/test across parallel agents (shared obj/bin).
# Usage: & .\scripts\build-lock.ps1 -Run build
#        & .\scripts\build-lock.ps1 -Run test -Filter "FullyQualifiedName~Fill"
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$lockDir = Join-Path $root 'tmp'
New-Item -ItemType Directory -Force -Path $lockDir | Out-Null
$lockPath = Join-Path $lockDir 'build.lock'
$deadline = (Get-Date).AddSeconds($TimeoutSeconds)
$stream = $null
while ($null -eq $stream) {
    try {
        $stream = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::OpenOrCreate,
            [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    } catch {
        if ((Get-Date) -gt $deadline) { throw ('build lock timeout: ' + $lockPath) }
        Start-Sleep -Seconds 3
    }
}
$started = Get-Date
try {
    Push-Location $root
    try {
        if ($Run -eq 'build' -or $Run -eq 'both') {
            & dotnet build UNCAD.slnx --nologo
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
        if ($Run -eq 'test' -or $Run -eq 'both') {
            $testArgs = @('test','UNCAD.slnx','--no-restore','--nologo')
            if ($Filter) { $testArgs += @('--filter', $Filter) }
            & dotnet @testArgs
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        }
    } finally { Pop-Location }
    Write-Host ('[build-lock] OK in ' + [math]::Round(((Get-Date) - $started).TotalSeconds,1) + 's')
} finally {
    $stream.Dispose()
}
