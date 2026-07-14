param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SelfContained = $true,
    [string]$AppVersion,
    [string]$PackageName
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$versionPattern = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'
if ([string]::IsNullOrWhiteSpace($AppVersion)) {
    throw "必须通过 -AppVersion 提供版本号，例如：-AppVersion `"1.0.1`"。"
}
if ($AppVersion.StartsWith("v", [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "AppVersion 不要带 v 前缀。"
}
if ($AppVersion -notmatch $versionPattern) {
    throw "AppVersion 格式无效：$AppVersion。请使用不带前导零的 N.N.N 稳定版本号。"
}
if ($Runtime -ne "win-x64") {
    throw "自动更新发布包仅支持 win-x64，当前 Runtime：$Runtime。"
}

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $root "artifacts"))
$desktopProject = Join-Path $root "src\IGoLibrary.Ex.Desktop\IGoLibrary.Ex.Desktop.csproj"
$updaterProject = Join-Path $root "src\IGoLibrary.Ex.Updater\IGoLibrary.Ex.Updater.csproj"
$output = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "publish\$Runtime"))
$updaterOutput = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "publish\updater-$Runtime"))
$packageOutput = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "windows\$Runtime"))
$manifestPath = Join-Path $output "update-manifest.json"

if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $PackageName = "IGoLibrary-Ex-v$AppVersion-windows-x64.zip"
}
$expectedPackageName = "IGoLibrary-Ex-v$AppVersion-windows-x64.zip"
if ($PackageName -cne $expectedPackageName) {
    throw "Windows 自动更新包名称必须为 $expectedPackageName。"
}
$zipPath = Join-Path $packageOutput $PackageName

function Remove-SafeBuildDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $artifactsPrefix = $artifactsRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($artifactsPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理 artifacts 之外的目录：$fullPath"
    }
    if ($fullPath -eq $artifactsRoot) {
        throw "拒绝清理 artifacts 根目录。"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

Remove-SafeBuildDirectory -Path $output
Remove-SafeBuildDirectory -Path $updaterOutput
New-Item -ItemType Directory -Path $output -Force | Out-Null
New-Item -ItemType Directory -Path $updaterOutput -Force | Out-Null
New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null

dotnet publish $desktopProject `
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
    throw "Desktop dotnet publish failed with exit code $LASTEXITCODE."
}

$prepareCloudflared = Join-Path $PSScriptRoot "prepare-cloudflared.ps1"
$cloudflaredDestination = Join-Path $output "tools\cloudflared"
& $prepareCloudflared -Runtime $Runtime -DestinationDirectory $cloudflaredDestination

dotnet publish $updaterProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained:true `
    -p:PublishSingleFile=true `
    -p:PublishTrimmed=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:UseSharedCompilation=false `
    -p:Version=$AppVersion `
    -p:InformationalVersion=$AppVersion `
    -o $updaterOutput
if ($LASTEXITCODE -ne 0) {
    throw "Updater dotnet publish failed with exit code $LASTEXITCODE."
}

$publishedExecutable = Join-Path $output "IGoLibrary.Ex.Desktop.exe"
$publishedUpdater = Join-Path $updaterOutput "IGoLibrary.Ex.Updater.exe"
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published executable was not found: $publishedExecutable"
}
if (-not (Test-Path -LiteralPath $publishedUpdater -PathType Leaf)) {
    throw "Published updater was not found: $publishedUpdater"
}
Copy-Item -LiteralPath $publishedUpdater -Destination (Join-Path $output "IGoLibrary.Ex.Updater.exe") -Force

$portableMarkerPath = Join-Path $output "portable-release.marker"
[System.IO.File]::WriteAllText(
    $portableMarkerPath,
    "IGoLibrary-Ex|portable|win-x64|2",
    [System.Text.UTF8Encoding]::new($false))

$manifestFiles = @(
    Get-ChildItem -LiteralPath $output -File -Recurse | ForEach-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($output, $_.FullName).Replace('\', '/')
        if ($relativePath -ceq "update-manifest.json") {
            return
        }

        [pscustomobject][ordered]@{
            path = $relativePath
            size = [long]$_.Length
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        }
    } | Sort-Object { $_.path.ToUpperInvariant() }, path
)

$manifest = [pscustomobject][ordered]@{
    schemaVersion = 2
    product = "IGoLibrary-Ex"
    version = $AppVersion
    runtime = "win-x64"
    entryExecutable = "IGoLibrary.Ex.Desktop.exe"
    files = $manifestFiles
}
$manifestJson = $manifest | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText(
    $manifestPath,
    $manifestJson,
    [System.Text.UTF8Encoding]::new($false))

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $output,
    $zipPath,
    [System.IO.Compression.CompressionLevel]::Optimal,
    $false)

$verifyScript = Join-Path $PSScriptRoot "verify-windows-package.ps1"
& $verifyScript -PackagePath $zipPath

$zipInfo = Get-Item -LiteralPath $zipPath
$zipHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Published desktop app to $output"
Write-Host "Created Windows zip at $zipPath"
Write-Host "ZIP size: $($zipInfo.Length) bytes"
Write-Host "ZIP SHA-256: $zipHash"
Write-Host "首次启用自动更新时，用户仍需手动安装一次此完整绿色版。"
