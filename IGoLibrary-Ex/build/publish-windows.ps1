param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $true,
    [string]$PackageName
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root "src\IGoLibrary.Ex.Desktop\IGoLibrary.Ex.Desktop.csproj"
$output = Join-Path $root "artifacts\publish\$Runtime"
$packageOutput = Join-Path $root "artifacts\windows\$Runtime"
if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $runtimeLabel = $Runtime -replace "^win-", ""
    $PackageName = "IGoLibrary-Ex-Windows-$runtimeLabel.zip"
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
