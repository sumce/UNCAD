[CmdletBinding()]
param(
    [ValidateSet("Menu", "InstallUser", "InstallAll", "VerifyUser", "VerifyAll", "VerifyPackage", "UninstallUser", "UninstallAll")]
    [string]$Mode = "Menu",
    [switch]$ElevatedChild
)

$ErrorActionPreference = "Stop"
$ScriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$SourceBundle = Join-Path $ScriptRoot "bundle\UNCAD.bundle"
$UserBundle = Join-Path $env:APPDATA "Autodesk\ApplicationPlugins\UNCAD.bundle"
$MachineBundle = Join-Path $env:ProgramData "Autodesk\ApplicationPlugins\UNCAD.bundle"
$LogPath = Join-Path $env:TEMP ("UNCAD-Setup-{0}.log" -f (Get-Date -Format "yyyyMMdd-HHmmss"))

function Write-SetupLog {
    param([string]$Message, [ConsoleColor]$Color = [ConsoleColor]::Gray)
    $line = "[{0}] {1}" -f (Get-Date -Format "yyyy-MM-dd HH:mm:ss"), $Message
    Add-Content -LiteralPath $LogPath -Value $line -Encoding UTF8
    Write-Host $Message -ForegroundColor $Color
}

function Test-IsAdministrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Assert-BundleChecksums {
    param([string]$BundlePath, [string[]]$AllowedPayloadFiles)
    $checksumPath = Join-Path $BundlePath "checksums.sha256"
    if (-not (Test-Path $checksumPath -PathType Leaf)) {
        throw "Bundle checksum manifest is missing: checksums.sha256"
    }

    $root = [IO.Path]::GetFullPath($BundlePath).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $expected = @{}
    $lineNumber = 0
    foreach ($line in [IO.File]::ReadAllLines($checksumPath)) {
        $lineNumber++
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        if ($line -notmatch '^(?<hash>[0-9A-Fa-f]{64})  (?<path>.+)$') {
            throw "Invalid checksum entry at line $lineNumber."
        }
        $relative = $Matches.path.Replace('/', [IO.Path]::DirectorySeparatorChar)
        if ([IO.Path]::IsPathRooted($relative) -or @($relative.Split([IO.Path]::DirectorySeparatorChar)) -contains '..') {
            throw "Unsafe checksum path at line $lineNumber."
        }
        $fullPath = [IO.Path]::GetFullPath((Join-Path $BundlePath $relative))
        if (-not $fullPath.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Checksum path escapes the bundle at line $lineNumber."
        }
        if ($expected.ContainsKey($relative)) { throw "Duplicate checksum entry: $relative" }
        if (-not (Test-Path $fullPath -PathType Leaf)) { throw "Checksummed file is missing: $relative" }
        $actualHash = (Get-FileHash -LiteralPath $fullPath -Algorithm SHA256).Hash
        if ($actualHash -ne $Matches.hash) { throw "SHA-256 mismatch: $relative" }
        $expected[$relative] = $true
    }

    $actualFiles = @(Get-ChildItem -LiteralPath $BundlePath -File -Recurse |
        Where-Object { $_.FullName -ne $checksumPath })
    foreach ($file in $actualFiles) {
        $relative = $file.FullName.Substring($root.Length)
        if ($AllowedPayloadFiles -notcontains $relative) {
            throw "Unexpected bundle payload file: $relative"
        }
        if (-not $expected.ContainsKey($relative)) { throw "File is missing from checksums.sha256: $relative" }
    }
    if ($expected.Count -ne $actualFiles.Count) { throw "Bundle checksum file count mismatch." }
}

function Get-AutoCAD2022Path {
    $candidates = New-Object System.Collections.Generic.List[string]
    $roots = @(
        "HKLM:\SOFTWARE\Autodesk\AutoCAD\R24.1",
        "HKLM:\SOFTWARE\WOW6432Node\Autodesk\AutoCAD\R24.1"
    )
    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }
        foreach ($key in Get-ChildItem $root -Recurse -ErrorAction SilentlyContinue) {
            $location = (Get-ItemProperty $key.PSPath -ErrorAction SilentlyContinue).AcadLocation
            if (-not [string]::IsNullOrWhiteSpace($location)) { $candidates.Add($location.Trim()) }
        }
    }
    $candidates.Add((Join-Path $env:ProgramFiles "Autodesk\AutoCAD 2022"))
    $programFilesX86 = [Environment]::GetEnvironmentVariable("ProgramFiles(x86)")
    if ($programFilesX86) { $candidates.Add((Join-Path $programFilesX86 "Autodesk\AutoCAD 2022")) }
    foreach ($path in $candidates | Select-Object -Unique) {
        if (Test-Path (Join-Path $path "acad.exe")) { return $path }
    }
    return $null
}

function Get-PackageInfo {
    param([string]$BundlePath, [switch]$AllowMissingChecksums)
    if (-not (Test-Path $BundlePath -PathType Container)) { throw "Bundle folder not found: $BundlePath" }
    $manifestPath = Join-Path $BundlePath "PackageContents.xml"
    if (-not (Test-Path $manifestPath -PathType Leaf)) { throw "PackageContents.xml is missing." }
    [xml]$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8
    $package = $manifest.ApplicationPackage
    if ($null -eq $package) { throw "PackageContents.xml has no ApplicationPackage root." }
    $entry = $package.Components.ComponentEntry
    $requirements = $package.Components.RuntimeRequirements
    if ($null -eq $entry) { throw "PackageContents.xml has no ComponentEntry." }
    if ($requirements.SeriesMin -ne "R24.1" -or $requirements.SeriesMax -ne "R24.1") {
        throw "This package is not restricted to AutoCAD 2022 (R24.1)."
    }
    if ([string]::IsNullOrWhiteSpace([string]$package.ProductCode) -or
        [string]::IsNullOrWhiteSpace([string]$package.UpgradeCode) -or
        $package.ProductCode -eq $package.UpgradeCode) {
        throw "Package product and upgrade identifiers are missing or invalid."
    }
    if ([string]$entry.LoadOnAutoCADStartup -ne "True" -or
        [string]$entry.LoadOnCommandInvocation -ne "True") {
        throw "Package must support both startup and command-triggered loading."
    }
    $declaredCommands = @($entry.Commands.Command | ForEach-Object { [string]$_.Global })
    $duplicateCommands = @($declaredCommands | Group-Object |
        Where-Object { $_.Count -gt 1 } | ForEach-Object { $_.Name })
    if ($duplicateCommands.Count -gt 0) {
        throw "Package declares duplicate public commands: $($duplicateCommands -join ', ')"
    }
    # The manifest is an explicit public API: only the current U1 family and seven retained
    # keyboard compatibility commands may trigger package loading. Keep this list in exact
    # sync with CommandIds.Registered; BundleLoadingTests enforces that contract.
    $expectedCommands = @(
        "U1L", "U1LX", "U1R", "U1Q1", "U1Q2", "U1Q4", "U1F", "U1U", "U1S", "U1C", "U1A", "U1SET", "U1DWG", "XLAYOUT", "XSTS", "Xmerge", "U1HELP",
        "UNL", "UNLX", "UNR", "UNQ1", "UNQ2", "UNQ4", "UNADD")
    foreach ($requiredCommand in $expectedCommands) {
        if ($declaredCommands -notcontains $requiredCommand) {
            throw "Package command-triggered loading is missing: $requiredCommand"
        }
    }
    $unexpectedCommands = @($declaredCommands | Where-Object { $expectedCommands -notcontains $_ })
    if ($unexpectedCommands.Count -gt 0) {
        $commandList = $unexpectedCommands -join ', '
        throw "Package declares unsupported public commands: $commandList"
    }
    $moduleRelative = ([string]$entry.ModuleName).Replace("/", "\").TrimStart([char[]]".\")
    $modulePath = Join-Path $BundlePath $moduleRelative
    if (-not (Test-Path $modulePath -PathType Leaf)) { throw "Plugin module is missing: $moduleRelative" }
    $payloadFiles = @(
        "PackageContents.xml",
        "UNCAD.dll", "NPOI.dll", "NPOI.OOXML.dll", "NPOI.OpenXml4Net.dll",
        "NPOI.OpenXmlFormats.dll", "ICSharpCode.SharpZipLib.dll", "BouncyCastle.Crypto.dll",
        "BOQ_Template.xlsx", "Resources\XFrameTemplate.dwg"
    )
    foreach ($file in $payloadFiles) {
        if (-not (Test-Path (Join-Path $BundlePath $file) -PathType Leaf)) { throw "Required file is missing: $file" }
    }
    $checksumPath = Join-Path $BundlePath "checksums.sha256"
    if (Test-Path $checksumPath -PathType Leaf) {
        Assert-BundleChecksums $BundlePath $payloadFiles
    }
    elseif (-not $AllowMissingChecksums) {
        throw "Bundle checksum manifest is missing: checksums.sha256"
    }
    $version = [string]$package.AppVersion
    $licenseMode = [string]$package.LicenseMode
    $licenseExpiresUtc = [string]$package.LicenseExpiresUtc
    if ($licenseMode -ne "Online") {
        throw "Only the unified online UNCAD Pro package is supported."
    }
    if (-not [string]::IsNullOrWhiteSpace($licenseExpiresUtc)) {
        throw "Online package expiry must come from pro.key, not PackageContents.xml."
    }
    $fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($modulePath).FileVersion
    # Accept exact four-part patch versions such as 1.9.7.1; retain compatibility with three-part packages.
    if ($fileVersion -ne $version -and $fileVersion -ne ($version + ".0")) {
        throw "Version mismatch: Package=$version, DLL=$fileVersion"
    }
    # Surface the service-pack suffix carried in the assembly's
    # informational version; AppVersion in PackageContents.xml stays numeric.
    $productVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($modulePath).ProductVersion
    $spSuffix = ""
    if ($productVersion -match "(SP\d+)") { $spSuffix = " " + $Matches[1] }
    [pscustomobject]@{
        Name = [string]$package.Name
        Version = ($version + $spSuffix)
        FileVersion = $fileVersion
        LicenseMode = $licenseMode
        LicenseExpiresUtc = $licenseExpiresUtc
        CustomerCode = [string]$package.CustomerCode
        LicenseeCompany = [string]$package.LicenseeCompany
        LicenseeName = [string]$package.LicenseeName
        ExpectedAuthorizationYears = [string]$package.ExpectedAuthorizationYears
        ModulePath = $modulePath
        BundlePath = $BundlePath
    }
}

function Compare-BundleFiles {
    param([string]$ExpectedRoot, [string]$ActualRoot)
    $expected = @(Get-ChildItem -LiteralPath $ExpectedRoot -File -Recurse)
    $actual = @(Get-ChildItem -LiteralPath $ActualRoot -File -Recurse)
    if ($expected.Count -ne $actual.Count) { throw "File count mismatch: expected $($expected.Count), found $($actual.Count)." }
    foreach ($sourceFile in $expected) {
        $relative = $sourceFile.FullName.Substring($ExpectedRoot.Length).TrimStart([IO.Path]::DirectorySeparatorChar)
        $targetFile = Join-Path $ActualRoot $relative
        if (-not (Test-Path $targetFile -PathType Leaf)) { throw "Installed file is missing: $relative" }
        $sourceHash = (Get-FileHash -LiteralPath $sourceFile.FullName -Algorithm SHA256).Hash
        $targetHash = (Get-FileHash -LiteralPath $targetFile -Algorithm SHA256).Hash
        if ($sourceHash -ne $targetHash) { throw "SHA-256 mismatch: $relative" }
    }
}

function Unblock-BundleFiles {
    param([string]$BundlePath)
    if (-not (Test-Path $BundlePath -PathType Container)) { return }
    Get-ChildItem -LiteralPath $BundlePath -File -Recurse -ErrorAction SilentlyContinue |
        Unblock-File -ErrorAction SilentlyContinue
}

function Assert-AutoCADClosed {
    if (Get-Process acad -ErrorAction SilentlyContinue) {
        throw "AutoCAD is running. Save all drawings and close AutoCAD before continuing."
    }
}

# 等待目录内所有文件可被独占打开：AutoCAD 退出后 DLL 句柄可能仍被短暂占用，
# 直接覆盖会失败并留下残缺安装目录。轮询直到锁释放或超时。
function Assert-TargetUnlocked {
    param([string]$Path, [int]$TimeoutSeconds = 180)
    if (-not (Test-Path $Path)) { return }
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $blocked = $false
        try {
            $files = @(Get-ChildItem -LiteralPath $Path -Recurse -File -ErrorAction Stop)
        }
        catch {
            throw "无法检查安装目录：$Path。请确认目录存在且当前账户有读写权限。原因：$($_.Exception.Message)"
        }
        $files | ForEach-Object {
            try {
                $stream = [IO.File]::Open($_.FullName, [IO.FileMode]::Open,
                    [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
                $stream.Close()
            }
            catch [UnauthorizedAccessException] {
                throw "安装目录或文件没有读写权限：$($_.FullName)"
            }
            catch { $blocked = $true }
        }
        if (-not $blocked) { return }
        Start-Sleep -Seconds 5
    }
    throw "安装目录仍被其他程序占用：$Path。请关闭 AutoCAD 及占用该目录的程序后重试。"
}

function Assert-NoScopeConflict {
    param([ValidateSet("User", "Machine")][string]$Scope)
    $other = if ($Scope -eq "User") { $MachineBundle } else { $UserBundle }
    if (Test-Path $other) {
        throw "Another UNCAD installation exists at $other. Uninstall it first to avoid duplicate AutoCAD loading."
    }
}

function Recover-InterruptedInstall {
    param([string]$Parent, [string]$Destination)

    # 进程中断可能留下 installing/backup 目录；先恢复可验证的目录，再清理残留。
    $candidates = @(
        Get-ChildItem -LiteralPath $Parent -Directory -Filter "UNCAD.bundle.installing.*" -ErrorAction SilentlyContinue
        Get-ChildItem -LiteralPath $Parent -Directory -Filter "UNCAD.bundle.backup.*" -ErrorAction SilentlyContinue
    ) | Sort-Object LastWriteTime -Descending
    if ($candidates.Count -eq 0) { return }

    $destinationValid = $false
    if (Test-Path $Destination) {
        try {
            Get-PackageInfo $Destination -AllowMissingChecksums | Out-Null
            $destinationValid = $true
        }
        catch { }
    }
    if (-not $destinationValid) {
        foreach ($candidate in $candidates) {
            try {
                $allowMissing = $candidate.Name -like "UNCAD.bundle.backup.*"
                Get-PackageInfo $candidate.FullName `
                    -AllowMissingChecksums:$allowMissing | Out-Null
                if (Test-Path $Destination) {
                    Assert-TargetUnlocked $Destination 60
                    Remove-Item -LiteralPath $Destination -Recurse -Force
                }
                Move-Item -LiteralPath $candidate.FullName -Destination $Destination
                Get-PackageInfo $Destination `
                    -AllowMissingChecksums:$allowMissing | Out-Null
                Write-SetupLog "Recovered interrupted installation from $($candidate.Name)." Yellow
                $destinationValid = $true
                break
            }
            catch {
                Write-SetupLog "Ignored invalid recovery candidate $($candidate.FullName): $($_.Exception.Message)" DarkYellow
            }
        }
    }

    if ($destinationValid) {
        foreach ($candidate in $candidates) {
            if (Test-Path $candidate.FullName) {
                try { Remove-Item -LiteralPath $candidate.FullName -Recurse -Force }
                catch { Write-SetupLog "Could not remove stale installer directory $($candidate.FullName): $($_.Exception.Message)" DarkYellow }
            }
        }
    }
}

# 清除 AutoCAD 的按需加载命令注册缓存(HKCU/HKLM 下各配置文件的
# Applications\UNCAD 键)。历次安装/升级会在这里累积陈旧命令条目,
# 导致新命令"未知命令"或已删除命令仍可调用;删除后下次启动
# AutoCAD 会依据 bundle 重新生成干净的缓存。
function Clear-CommandRegistrationCache {
    $removed = 0
    foreach ($hive in @("HKCU:", "HKLM:")) {
        # Cover both the native and the WOW6432Node (32-bit) registry views so a
        # stale cache written under either view cannot survive the upgrade.
        $rootPaths = @(
            (Join-Path $hive "SOFTWARE\Autodesk\AutoCAD"),
            (Join-Path $hive "SOFTWARE\WOW6432Node\Autodesk\AutoCAD")
        )
        foreach ($rootPath in $rootPaths) {
            $releases = Get-ChildItem $rootPath `
                -ErrorAction SilentlyContinue
            foreach ($release in $releases) {
                $versions = Get-ChildItem $release.PSPath -ErrorAction SilentlyContinue
                foreach ($version in $versions) {
                    $cacheKey = Join-Path $version.PSPath "Applications\UNCAD"
                    if (-not (Test-Path $cacheKey)) { continue }
                    try {
                        Remove-Item -LiteralPath $cacheKey -Recurse -Force `
                            -ErrorAction Stop
                        $removed++
                        Write-SetupLog "Cleared stale command registration cache: $cacheKey" DarkGray
                    }
                    catch {
                        Write-SetupLog ("Could not clear command cache " + $cacheKey `
                            + " ( HKLM needs administrator): $($_.Exception.Message)") DarkYellow
                    }
                }
            }
        }
    }
    if ($removed -eq 0) {
        Write-SetupLog "No stale command registration cache found." DarkGray
    }
}

function Install-Bundle {
    param([ValidateSet("User", "Machine")][string]$Scope)
    Assert-AutoCADClosed
    $cadPath = Get-AutoCAD2022Path
    if (-not $cadPath) { throw "AutoCAD 2022 was not detected. Installation stopped." }
    Assert-NoScopeConflict $Scope
    Unblock-BundleFiles $SourceBundle
    $sourceInfo = Get-PackageInfo $SourceBundle
    $destination = if ($Scope -eq "User") { $UserBundle } else { $MachineBundle }
    $parent = Split-Path -Parent $destination
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    Recover-InterruptedInstall $parent $destination
    Assert-TargetUnlocked $destination
    $stage = Join-Path $parent ("UNCAD.bundle.installing." + $PID)
    $backup = Join-Path $parent ("UNCAD.bundle.backup." + (Get-Date -Format "yyyyMMddHHmmss"))
    $oldMoved = $false
    $newPlaced = $false
    Write-SetupLog "Package: $($sourceInfo.Name) $($sourceInfo.Version)" Cyan
    Write-SetupLog "AutoCAD 2022: $cadPath" Cyan
    Write-SetupLog "Target: $destination" Cyan
    try {
        if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
        New-Item -ItemType Directory -Path $stage -Force | Out-Null
        Copy-Item -Path (Join-Path $SourceBundle "*") -Destination $stage -Recurse -Force
        Unblock-BundleFiles $stage
        Get-PackageInfo $stage | Out-Null
        Compare-BundleFiles $SourceBundle $stage
        Write-SetupLog "Staged files passed manifest, version, and SHA-256 validation." Green
        if (Test-Path $destination) {
            Move-Item -LiteralPath $destination -Destination $backup
            $oldMoved = $true
            Write-SetupLog "Previous installation backed up." DarkGray
        }
        Move-Item -LiteralPath $stage -Destination $destination
        $newPlaced = $true
        Unblock-BundleFiles $destination
        Get-PackageInfo $destination | Out-Null
        Compare-BundleFiles $SourceBundle $destination
        if ($oldMoved -and (Test-Path $backup)) { Remove-Item $backup -Recurse -Force }
        Write-SetupLog "INSTALLATION SUCCESSFUL: UNCAD $($sourceInfo.Version)" Green
        Write-SetupLog "Downloaded-file security marks were removed from the installed bundle." Green
        Write-SetupLog $(if ($Scope -eq "User") {
            "Scope: current Windows user only. Use InstallAll on shared computers."
        } else { "Scope: all Windows users on this computer." }) Cyan
        Write-SetupLog "Restart AutoCAD 2022. The UNCAD tab registers automatically; use AutoCAD RIBBON if hidden." Green
        Clear-CommandRegistrationCache
    }
    catch {
        $failure = $_
        $rollbackErrors = New-Object System.Collections.Generic.List[string]
        try {
            if (Test-Path $stage) { Remove-Item -LiteralPath $stage -Recurse -Force -ErrorAction Stop }
        }
        catch { $rollbackErrors.Add("清理临时目录失败：$($_.Exception.Message)") }
        if ($newPlaced -and (Test-Path $destination)) {
            try { Remove-Item -LiteralPath $destination -Recurse -Force -ErrorAction Stop }
            catch { $rollbackErrors.Add("清理新安装目录失败：$($_.Exception.Message)") }
        }
        if ($oldMoved -and (Test-Path $backup)) {
            try {
                Assert-TargetUnlocked $backup 60
                if (Test-Path $destination) {
                    Assert-TargetUnlocked $destination 60
                    Remove-Item -LiteralPath $destination -Recurse -Force -ErrorAction Stop
                }
                Move-Item -LiteralPath $backup -Destination $destination -ErrorAction Stop
                Get-PackageInfo $destination -AllowMissingChecksums | Out-Null
                Write-SetupLog "Previous installation was restored and verified." Yellow
            }
            catch { $rollbackErrors.Add("回滚失败：$($_.Exception.Message)") }
        }
        if ($rollbackErrors.Count -gt 0) {
            $details = $rollbackErrors -join "；"
            Write-SetupLog "安装失败且回滚未完成：$details" Red
            throw "安装失败：$($failure.Exception.Message)。$details"
        }
        throw $failure
    }
}

function Verify-Bundle {
    param([ValidateSet("User", "Machine", "Package")][string]$Scope)
    $path = if ($Scope -eq "User") { $UserBundle } elseif ($Scope -eq "Machine") { $MachineBundle } else { $SourceBundle }
    $info = Get-PackageInfo $path
    if ($Scope -ne "Package") { Compare-BundleFiles $SourceBundle $path }
    $cadPath = Get-AutoCAD2022Path
    if (-not $cadPath) { throw "Bundle is valid, but AutoCAD 2022 was not detected." }
    Write-SetupLog "VERIFICATION PASSED: $($info.Name) $($info.Version)" Green
    Write-SetupLog "Files: $(@(Get-ChildItem $path -File -Recurse).Count), all SHA-256 checks passed." Green
    Write-SetupLog "AutoCAD 2022: $cadPath" Green
    Write-SetupLog "Location: $path" Cyan
}

function Uninstall-Bundle {
    param([ValidateSet("User", "Machine")][string]$Scope)
    Assert-AutoCADClosed
    $destination = if ($Scope -eq "User") { $UserBundle } else { $MachineBundle }
    if (-not (Test-Path $destination)) {
        Write-SetupLog "UNCAD is not installed at: $destination" Yellow
    }
    else {
        Assert-TargetUnlocked $destination
        Remove-Item -LiteralPath $destination -Recurse -Force
        if (Test-Path $destination) { throw "Uninstall verification failed: folder still exists." }
    }
    Clear-CommandRegistrationCache
    Write-SetupLog "UNINSTALLATION CLEANUP COMPLETE: $destination" Green
}

function Invoke-ElevatedMode {
    param([string]$RequestedMode)
    $powershell = Join-Path $PSHOME "powershell.exe"
    $arguments = '-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '" -Mode ' + $RequestedMode + ' -ElevatedChild'
    Write-SetupLog "Requesting Administrator permission for $RequestedMode..." Yellow
    $process = Start-Process -FilePath $powershell -ArgumentList $arguments -Verb RunAs -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Elevated operation failed or was cancelled (exit $($process.ExitCode))." }
}

function Get-InstalledLabel {
    param([string]$Path)
    if (-not (Test-Path $Path)) { return "Not installed" }
    try {
        return "Installed " + (Get-PackageInfo $Path -AllowMissingChecksums).Version
    }
    catch { return "Invalid installation" }
}

function Pause-Setup {
    Write-Host ""
    [void](Read-Host "Press Enter to continue")
}

function Show-Menu {
    while ($true) {
        Clear-Host
        $packageLabel = try { $p = Get-PackageInfo $SourceBundle; "$($p.Name) $($p.Version)" } catch { "INVALID PACKAGE" }
        $cad = Get-AutoCAD2022Path
        Write-Host "============================================================" -ForegroundColor DarkCyan
        Write-Host " UNCAD Setup - UNSIAO.Ltd" -ForegroundColor Cyan
        Write-Host "============================================================" -ForegroundColor DarkCyan
        Write-Host "Package      : $packageLabel"
        Write-Host "AutoCAD 2022 : $(if ($cad) { $cad } else { 'Not detected' })"
        Write-Host "Current user : $(Get-InstalledLabel $UserBundle)"
        Write-Host "All users    : $(Get-InstalledLabel $MachineBundle)"
        Write-Host "AutoCAD open : $([bool](Get-Process acad -ErrorAction SilentlyContinue))"
        Write-Host "Log          : $LogPath" -ForegroundColor DarkGray
        Write-Host ""
        Write-Host "  1. Install or repair for current user (recommended)"
        Write-Host "  2. Verify current-user installation"
        Write-Host "  3. Install or repair for all users (Administrator)"
        Write-Host "  4. Verify release package"
        Write-Host "  5. Uninstall current-user installation"
        Write-Host "  6. Uninstall all-users installation (Administrator)"
        Write-Host "  0. Exit"
        Write-Host ""
        $choice = Read-Host "Select"
        try {
            switch ($choice) {
                "1" { Install-Bundle User }
                "2" { Verify-Bundle User }
                "3" { if (Test-IsAdministrator) { Install-Bundle Machine } else { Invoke-ElevatedMode InstallAll } }
                "4" { Verify-Bundle Package }
                "5" { Uninstall-Bundle User }
                "6" { if (Test-IsAdministrator) { Uninstall-Bundle Machine } else { Invoke-ElevatedMode UninstallAll } }
                "0" { return }
                default { Write-SetupLog "Unknown selection: $choice" Yellow }
            }
        }
        catch { Write-SetupLog ("FAILED: " + $_.Exception.Message) Red }
        Pause-Setup
    }
}

try {
    switch ($Mode) {
        "Menu" { Show-Menu }
        "InstallUser" { Install-Bundle User }
        "InstallAll" { if (Test-IsAdministrator) { Install-Bundle Machine } elseif ($ElevatedChild) { throw "Administrator permission was not granted." } else { Invoke-ElevatedMode InstallAll } }
        "VerifyUser" { Verify-Bundle User }
        "VerifyAll" { Verify-Bundle Machine }
        "VerifyPackage" { Verify-Bundle Package }
        "UninstallUser" { Uninstall-Bundle User }
        "UninstallAll" { if (Test-IsAdministrator) { Uninstall-Bundle Machine } elseif ($ElevatedChild) { throw "Administrator permission was not granted." } else { Invoke-ElevatedMode UninstallAll } }
    }
    exit 0
}
catch {
    Write-SetupLog ("FAILED: " + $_.Exception.Message) Red
    Write-SetupLog "Details were written to: $LogPath" Yellow
    exit 1
}
