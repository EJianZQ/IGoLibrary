param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $true
)

$ErrorActionPreference = "Stop"

$root = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$project = Join-Path $root "src\IGoLibrary.Ex.Desktop\IGoLibrary.Ex.Desktop.csproj"
$output = Join-Path $root "artifacts\publish\$Runtime"

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained:$SelfContained `
    -p:PublishSingleFile=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -o $output

Write-Host "Published desktop app to $output"
Write-Host "To build the installer, open build\\IGoLibrary-Ex.iss in Inno Setup and compile it."
