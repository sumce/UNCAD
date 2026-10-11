# 把客户端安装包发布到服务端（安装包托管 + 标记为最新版本）。
#
# 为什么需要它：Release.DownloadUrl 一开始只是外部 URL 字符串，服务器上从未托管过
# 安装包 —— 「打包好了」却没有任何用户能下载。现在安装包由服务端自己托管
# （/api/releases/{version}/download），本脚本负责上传并标记最新版本，
# 服务端会自动把上一个「最新版本」降级（IsLatest 是排他标记）。
#
# 用法：
#   $env:UNCAD_ADMIN_PASSWORD = '<管理员密码>'
#   .\tools\publish-release.ps1 -Version 2.6.1.0 -ZipPath artifacts\Pro\UNCAD-Pro-v2.6.1.0.zip
#
#   # 发布说明（会显示在官网下载页/个人中心）
#   .\tools\publish-release.ps1 -Version 2.6.1.0 -ZipPath ... -NotesPath docs\RELEASE-2.6.1.md
#
#   # 只上传、不改「最新版本」标记
#   .\tools\publish-release.ps1 -Version 2.6.1.0 -ZipPath ... -NoLatest
param(
    [Parameter(Mandatory = $true)][string]$Version,
    [Parameter(Mandatory = $true)][string]$ZipPath,
    [string]$BaseUrl = 'https://uncad.aosa.me',
    [string]$Identifier = 'admin',
    [string]$Password,
    [string]$NotesPath,
    [switch]$NoLatest
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')
if ([string]::IsNullOrWhiteSpace($Password)) { $Password = $env:UNCAD_ADMIN_PASSWORD }
if ([string]::IsNullOrWhiteSpace($Password)) {
    throw '缺少管理员密码：请设置环境变量 UNCAD_ADMIN_PASSWORD，或用 -Password 传入。'
}
if (!(Test-Path -LiteralPath $ZipPath)) { throw "找不到安装包：$ZipPath" }

$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $ZipPath).Path)
$sha = (Get-FileHash -LiteralPath $ZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ("安装包：{0}  {1:N2} MB  SHA-256={2}" -f $ZipPath, ($bytes.Length / 1MB), $sha)

# 用 HttpClient 而不是 Invoke-WebRequest：部分环境下 IWR 会退回 IE 引擎并报
# 「Windows PowerShell 处于非交互模式」，而发布脚本需要能无人值守跑。
Add-Type -AssemblyName System.Net.Http
$handler = New-Object System.Net.Http.HttpClientHandler
$handler.CookieContainer = New-Object System.Net.CookieContainer
$handler.UseCookies = $true
$client = New-Object System.Net.Http.HttpClient($handler)
$client.BaseAddress = [Uri]$BaseUrl
$client.Timeout = [TimeSpan]::FromMinutes(10)

function Send-Api {
    param([string]$Path, [string]$Method = 'GET', $Json, [byte[]]$Raw, [string]$ContentType)
    $request = New-Object System.Net.Http.HttpRequestMessage(
        (New-Object System.Net.Http.HttpMethod($Method)), $Path)
    if ($Raw) {
        $request.Content = New-Object System.Net.Http.ByteArrayContent -ArgumentList @(, $Raw)
        $request.Content.Headers.ContentType =
            [System.Net.Http.Headers.MediaTypeHeaderValue]::Parse($ContentType)
    } elseif ($null -ne $Json) {
        $request.Content = New-Object System.Net.Http.StringContent(
            ($Json | ConvertTo-Json -Compress), [System.Text.Encoding]::UTF8, 'application/json')
    }
    $response = $client.SendAsync($request).GetAwaiter().GetResult()
    $text = $response.Content.ReadAsStringAsync().GetAwaiter().GetResult()
    $data = $null
    if ($text) { try { $data = $text | ConvertFrom-Json } catch { } }
    if (!$response.IsSuccessStatusCode) {
        throw ("{0} {1} 失败：HTTP {2} {3}" -f $Method, $Path,
            [int]$response.StatusCode, $text)
    }
    return @{ Status = [int]$response.StatusCode; Text = $text; Data = $data }
}

Write-Host '1/4 登录管理员…'
Send-Api -Path '/api/auth/login' -Method POST `
    -Json @{ identifier = $Identifier; password = $Password } | Out-Null
Write-Host '    登录成功'

Write-Host "2/4 上传安装包（PUT /api/admin/releases/$Version/package）…"
$upload = Send-Api -Path "/api/admin/releases/$Version/package" -Method PUT `
    -Raw $bytes -ContentType 'application/zip'
$info = $upload.Data
if ($info.sha256 -ne $sha) { throw "服务端记录的 SHA-256 与本地不一致：$($info.sha256) vs $sha" }
if ([int64]$info.sizeBytes -ne $bytes.Length) {
    throw "服务端记录的字节数与本地不一致：$($info.sizeBytes) vs $($bytes.Length)"
}
Write-Host ("    已上传：{0} 字节  下载地址={1}  created={2}" -f `
    $info.sizeBytes, $info.downloadUrl, $info.created)

Write-Host '3/4 更新版本记录…'
$list = (Send-Api -Path '/api/admin/releases').Data
$row = @($list.items) | Where-Object { $_.version -eq $Version } | Select-Object -First 1
if (!$row) { throw "服务端版本列表里找不到 $Version（安装包上传应已自动创建）。" }

$update = @{ version = $Version; isEnabled = $true }
if (!$NoLatest) { $update.isLatest = $true }
if ($NotesPath) {
    if (!(Test-Path -LiteralPath $NotesPath)) { throw "找不到发布说明：$NotesPath" }
    # 必须显式按 UTF-8 读：Windows PowerShell 5.1 的 Get-Content 默认用 ANSI，
    # 会把 UTF-8 的中文读成乱码并原样存进服务端（已踩过）。
    $update.notes = [System.IO.File]::ReadAllText(
        (Resolve-Path -LiteralPath $NotesPath).Path,
        [System.Text.Encoding]::UTF8).Trim()
    if ($update.notes.Length -gt 4000) { $update.notes = $update.notes.Substring(0, 4000) }
}
$updated = (Send-Api -Path "/api/admin/releases/$($row.id)" -Method PUT -Json $update).Data
Write-Host ("    版本记录已更新：isLatest={0} isEnabled={1} 降级旧版本={2}" -f `
    $updated.item.isLatest, $updated.item.isEnabled, $updated.demoted)

Write-Host '4/4 校验公开下载链路…'
if (!$NoLatest) {
    # /api/releases/latest 返回扁平对象（version/notes/sha256/downloadUrl），
    # 不像管理端接口那样包在 item 里。
    $latest = (Send-Api -Path '/api/releases/latest').Data
    if ($latest.version -ne $Version) {
        throw "公开的最新版本不是 $Version，而是 $($latest.version)"
    }
    if ($latest.sha256 -ne $sha) {
        throw "公开的最新版本 SHA-256 不一致：$($latest.sha256)"
    }
    Write-Host "    /api/releases/latest -> $($latest.version)  SHA 一致"
}

$downloadClient = New-Object System.Net.Http.HttpClient
$downloadClient.Timeout = [TimeSpan]::FromMinutes(10)
$url = $BaseUrl + '/api/releases/' + $Version + '/download'
$downloaded = $downloadClient.GetByteArrayAsync($url).GetAwaiter().GetResult()
$downloadedSha = [System.BitConverter]::ToString(
    [System.Security.Cryptography.SHA256]::Create().ComputeHash($downloaded)).
    Replace('-', '').ToLowerInvariant()
if ($downloaded.Length -ne $bytes.Length -or $downloadedSha -ne $sha) {
    throw "公开下载的内容与本地不一致：$($downloaded.Length) 字节 sha=$downloadedSha"
}
Write-Host ("    未登录下载成功：{0} 字节  SHA-256 一致" -f $downloaded.Length)
Write-Host ''
Write-Host "完成：$Version 已托管在 $url"
