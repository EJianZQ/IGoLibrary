param(
    [string]$Configuration = "Release",
    [string]$AppVersion,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$versionPattern = '^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z-]*(\.[0-9A-Za-z][0-9A-Za-z-]*)*)?$'
if ([string]::IsNullOrWhiteSpace($AppVersion)) {
    throw "必须通过 -AppVersion 提供版本号，例如：-AppVersion `"0.4.0-beta.1`"。"
}
if ($AppVersion.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "AppVersion 不要带 v 前缀；请传 `"0.4.0-beta.1`"，Git tag / Release 再使用 `"v0.4.0-beta.1`"。"
}
if ($AppVersion -notmatch $versionPattern) {
    throw "AppVersion 格式无效：$AppVersion。请使用 0.4.0、0.4.0-beta.1 或 0.4.0-rc.1。"
}

$Root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$PublishScript = Join-Path $PSScriptRoot "publish-macos.ps1"
$OutputRoot = Join-Path $Root "artifacts\macos"

$targets = @(
    [pscustomobject]@{
        Runtime = "osx-arm64"
        DisplayName = "Apple Silicon"
        PackageName = "IGoLibrary-Ex-v$AppVersion-macOS-Apple-Silicon-arm64.zip"
    },
    [pscustomobject]@{
        Runtime = "osx-x64"
        DisplayName = "Intel"
        PackageName = "IGoLibrary-Ex-v$AppVersion-macOS-Intel-x64.zip"
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
    }

    if ($SkipPublish) {
        $parameters.SkipPublish = $true
    }

    & $PublishScript @parameters
}

Write-Host ""
Write-Host "macOS packages are ready:"
foreach ($target in $targets) {
    $packagePath = Join-Path (Join-Path $OutputRoot $target.Runtime) $target.PackageName
    Write-Host "  $($target.DisplayName): $packagePath"
}
