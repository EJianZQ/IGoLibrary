param(
    [string]$Configuration = "Release",
    [string]$AppVersion,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$versionPattern = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'
if ([string]::IsNullOrWhiteSpace($AppVersion)) {
    throw '必须通过 -AppVersion 提供版本号，例如：-AppVersion "1.0.1"。'
}
if ($AppVersion.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "AppVersion 不要带 v 前缀；Git tag / Release 再使用 vN.N.N。"
}
if ($AppVersion -notmatch $versionPattern) {
    throw "AppVersion 格式无效：$AppVersion。请使用不带前导零的 N.N.N 稳定版本号。"
}

$Root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$PublishScript = Join-Path $PSScriptRoot "publish-macos.ps1"
$OutputRoot = Join-Path $Root "artifacts\macos"

$targets = @(
    [pscustomobject]@{
        Runtime = "osx-arm64"
        DisplayName = "Apple Silicon"
        PackageName = "IGoLibrary-Ex-v$AppVersion-macOS-Apple-Silicon-arm64.zip"
        BundledPackageName = "IGoLibrary-Ex-v$AppVersion-macOS-Apple-Silicon-arm64-with-cloudflared.zip"
    },
    [pscustomobject]@{
        Runtime = "osx-x64"
        DisplayName = "Intel"
        PackageName = "IGoLibrary-Ex-v$AppVersion-macOS-Intel-x64.zip"
        BundledPackageName = "IGoLibrary-Ex-v$AppVersion-macOS-Intel-x64-with-cloudflared.zip"
    }
)

foreach ($target in $targets) {
    Write-Host ""
    Write-Host "Publishing macOS $($target.DisplayName) package ($($target.Runtime))..."

    $parameters = @{
        Configuration = $Configuration
        Runtime = $target.Runtime
        AppVersion = $AppVersion
        PackageName = $target.PackageName
        BundledPackageName = $target.BundledPackageName
    }

    if ($SkipPublish) {
        $parameters.SkipPublish = $true
    }

    & $PublishScript @parameters
}

Write-Host ""
Write-Host "macOS packages are ready:"
foreach ($target in $targets) {
    $runtimeOutput = Join-Path $OutputRoot $target.Runtime
    foreach ($package in @(
        [pscustomobject]@{ Label = 'lightweight'; Name = $target.PackageName },
        [pscustomobject]@{ Label = 'with cloudflared'; Name = $target.BundledPackageName }
    )) {
        $packagePath = Join-Path $runtimeOutput $package.Name
        $packageInfo = Get-Item -LiteralPath $packagePath
        $packageHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
        Write-Host "  $($target.DisplayName) $($package.Label): $packagePath"
        Write-Host "    Size: $($packageInfo.Length) bytes"
        Write-Host "    SHA-256: $packageHash"
    }
}
