param(
    [string]$Configuration = "Release",
    [string]$AppVersion = "1.0.0",
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$PublishScript = Join-Path $PSScriptRoot "publish-macos.ps1"
$OutputRoot = Join-Path $Root "artifacts\macos"

$targets = @(
    [pscustomobject]@{
        Runtime = "osx-arm64"
        DisplayName = "Apple Silicon"
        PackageName = "IGoLibrary-Ex-macOS-Apple-Silicon-arm64.zip"
    },
    [pscustomobject]@{
        Runtime = "osx-x64"
        DisplayName = "Intel"
        PackageName = "IGoLibrary-Ex-macOS-Intel-x64.zip"
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
