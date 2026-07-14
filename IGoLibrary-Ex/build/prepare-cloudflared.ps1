param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'osx-x64', 'osx-arm64')]
    [string]$Runtime,

    [Parameter(Mandatory = $true)]
    [string]$DestinationDirectory,

    [string]$CacheDirectory,

    [switch]$Offline
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$pathComparison = if ([System.OperatingSystem]::IsWindows()) {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}
$manifestPath = Join-Path $PSScriptRoot 'cloudflared-assets.json'
$manifest = Get-Content -Raw -LiteralPath $manifestPath | ConvertFrom-Json
$asset = $manifest.assets.$Runtime
if ($null -eq $asset) {
    throw "cloudflared 资产清单不支持 Runtime：$Runtime"
}

if ([string]::IsNullOrWhiteSpace($CacheDirectory)) {
    $CacheDirectory = Join-Path $root 'artifacts\cache\cloudflared'
}

$CacheDirectory = [System.IO.Path]::GetFullPath($CacheDirectory)
$DestinationDirectory = [System.IO.Path]::GetFullPath($DestinationDirectory)
$versionDirectory = Join-Path $CacheDirectory $manifest.version
$assetPath = Join-Path $versionDirectory $asset.fileName
$downloadUri = "https://github.com/cloudflare/cloudflared/releases/download/$($manifest.version)/$($asset.fileName)"

New-Item -ItemType Directory -Path $versionDirectory -Force | Out-Null
if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
    if ($Offline) {
        throw "未找到 cloudflared $($manifest.version) 的本地缓存：$assetPath。请先显式运行 build/prepare-cloudflared.ps1 下载并校验该资产。"
    }

    Write-Host "Downloading cloudflared $($manifest.version) for $Runtime..."
    $temporaryDownload = "$assetPath.download"
    try {
        Invoke-WebRequest -Uri $downloadUri -OutFile $temporaryDownload
        Move-Item -LiteralPath $temporaryDownload -Destination $assetPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporaryDownload) {
            Remove-Item -LiteralPath $temporaryDownload -Force
        }
    }
}

$actualHash = (Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($actualHash -ne $asset.sha256.ToLowerInvariant()) {
    throw "cloudflared 校验失败：$assetPath 的 SHA-256 为 $actualHash，预期为 $($asset.sha256)"
}

New-Item -ItemType Directory -Path $DestinationDirectory -Force | Out-Null
$destinationName = if ($Runtime -eq 'win-x64') { 'cloudflared.exe' } else { 'cloudflared' }
$destinationPath = Join-Path $DestinationDirectory $destinationName

if ($asset.archiveType -eq 'binary') {
    Copy-Item -LiteralPath $assetPath -Destination $destinationPath -Force
}
elseif ($asset.archiveType -eq 'tgz') {
    $extractDirectory = Join-Path $versionDirectory ("extract-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $extractDirectory | Out-Null
    try {
        $tar = Get-Command 'tar' -CommandType Application -ErrorAction Stop
        $tarArguments = @('-xzf', $assetPath, '-C', $extractDirectory)
        & $tar.Source @tarArguments
        $tarExitCode = $LASTEXITCODE
        if ($tarExitCode -ne 0) {
            throw "tar 解压 cloudflared 失败，退出码：$tarExitCode"
        }

        $extractedBinary = Get-ChildItem -LiteralPath $extractDirectory -Recurse -File |
            Where-Object { $_.Name -eq 'cloudflared' } |
            Select-Object -First 1
        if ($null -eq $extractedBinary) {
            throw 'cloudflared 压缩包中未找到可执行文件'
        }

        Copy-Item -LiteralPath $extractedBinary.FullName -Destination $destinationPath -Force
    }
    finally {
        if (Test-Path -LiteralPath $extractDirectory) {
            $resolvedCache = [System.IO.Path]::GetFullPath($versionDirectory).TrimEnd(
                [System.IO.Path]::DirectorySeparatorChar,
                [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
            $resolvedExtract = [System.IO.Path]::GetFullPath($extractDirectory)
            if (-not $resolvedExtract.StartsWith($resolvedCache, $pathComparison)) {
                throw "拒绝清理缓存目录之外的路径：$resolvedExtract"
            }

            Remove-Item -LiteralPath $resolvedExtract -Recurse -Force
        }
    }
}
else {
    throw "未知的 cloudflared 资产类型：$($asset.archiveType)"
}

$licenseSource = Join-Path $PSScriptRoot 'third-party\cloudflared-LICENSE.txt'
$noticeSource = Join-Path $PSScriptRoot 'third-party\THIRD-PARTY-NOTICES.txt'
Copy-Item -LiteralPath $licenseSource -Destination (Join-Path $DestinationDirectory 'LICENSE.txt') -Force
Copy-Item -LiteralPath $noticeSource -Destination (Join-Path $DestinationDirectory 'THIRD-PARTY-NOTICES.txt') -Force

Write-Host "Prepared cloudflared $($manifest.version) at $destinationPath"
