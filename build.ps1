param(
    [string]$Configuration = "Debug",
    [string]$AutoCADDir = "D:\Program Files\Autodesk\AutoCAD 2022",
    [switch]$SkipBundle,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

$buildArgs = @(
    "build", "$root\UNCAD.slnx", "-c", $Configuration,
    "-p:AutoCADDir=$AutoCADDir"
)
if ($NoRestore) { $buildArgs += "--no-restore" }
& dotnet $buildArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$dll = "$root\src\UNCAD\bin\$Configuration\net48\UNCAD.dll"
Write-Host "构建成功: $dll" -ForegroundColor Green

if (-not $SkipBundle) {
    $bundleDir = "$root\bundle\UNCAD.bundle"
    if (-not (Test-Path $bundleDir)) { New-Item -ItemType Directory -Path $bundleDir -Force | Out-Null }
    # 拷贝插件 DLL 与全部依赖（NPOI 等）到分发包
    Get-ChildItem "$root\src\UNCAD\bin\$Configuration\net48\*.dll" | Copy-Item -Destination $bundleDir -Force
    Write-Host "已更新分发包: $bundleDir" -ForegroundColor Green
    Write-Host "部署到用户机器：运行 install-user.bat（当前用户）或 install-all.bat（管理员，所有用户）" -ForegroundColor Yellow
}
