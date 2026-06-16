param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $true,
    [string]$AppVersion,
    [string]$PackageName
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

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root "src\IGoLibrary.Ex.Desktop\IGoLibrary.Ex.Desktop.csproj"
$output = Join-Path $root "artifacts\publish\$Runtime"
$packageOutput = Join-Path $root "artifacts\windows\$Runtime"
if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $runtimeLabel = $Runtime -replace "^win-", ""
    $PackageName = "IGoLibrary-Ex-v$AppVersion-windows-$runtimeLabel.zip"
}
$zipPath = Join-Path $packageOutput $PackageName

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained:$SelfContained `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:UsedAvaloniaProducts= `
    -p:UseSharedCompilation=false `
    -p:Version=$AppVersion `
    -p:InformationalVersion=$AppVersion `
    -o $output

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedExecutable = Join-Path $output "IGoLibrary.Ex.Desktop.exe"
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published executable was not found: $publishedExecutable"
}

New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Join-Path $output "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host "Published desktop app to $output"
Write-Host "Created Windows zip at $zipPath"
Write-Host "To build the installer, open build\\IGoLibrary-Ex.iss in Inno Setup and compile it."
