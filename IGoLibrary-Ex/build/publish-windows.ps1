param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$SelfContained = $true,
    [string]$AppVersion,
    [string]$PackageName,
    [string]$BundledPackageName
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$versionPattern = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$'
if ([string]::IsNullOrWhiteSpace($AppVersion)) {
    throw '必须通过 -AppVersion 提供版本号，例如：-AppVersion "1.0.1"。'
}
if ($AppVersion.StartsWith('v', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'AppVersion 不要带 v 前缀。'
}
if ($AppVersion -notmatch $versionPattern) {
    throw "AppVersion 格式无效：$AppVersion。请使用不带前导零的 N.N.N 稳定版本号。"
}
if ($Runtime -ne 'win-x64') {
    throw "自动更新发布包仅支持 win-x64，当前 Runtime：$Runtime。"
}

$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$pathComparison = if ([System.OperatingSystem]::IsWindows()) {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}
$desktopProject = Join-Path $root 'src\IGoLibrary.Ex.Desktop\IGoLibrary.Ex.Desktop.csproj'
$updaterProject = Join-Path $root 'src\IGoLibrary.Ex.Updater\IGoLibrary.Ex.Updater.csproj'
$output = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "publish\$Runtime"))
$updaterOutput = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "publish\updater-$Runtime"))
$packageOutput = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "windows\$Runtime"))
$lightweightStaging = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "staging\windows\$Runtime\no-tools"))
$packageStaging = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "staging\windows\$Runtime\packages"))
$manifestPath = Join-Path $output 'update-manifest.json'

$expectedPackageName = "IGoLibrary-Ex-v$AppVersion-windows-x64.zip"
$expectedBundledPackageName = "IGoLibrary-Ex-v$AppVersion-windows-x64-with-cloudflared.zip"
if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $PackageName = $expectedPackageName
}
if ([string]::IsNullOrWhiteSpace($BundledPackageName)) {
    $BundledPackageName = $expectedBundledPackageName
}
if ($PackageName -cne $expectedPackageName) {
    throw "Windows 自动更新包名称必须为 $expectedPackageName。"
}
if ($BundledPackageName -cne $expectedBundledPackageName) {
    throw "Windows cloudflared 完整包名称必须为 $expectedBundledPackageName。"
}
if ($PackageName -ieq $BundledPackageName) {
    throw '轻量包和 cloudflared 完整包不能使用同一个文件名。'
}

$zipPath = Join-Path $packageOutput $PackageName
$bundledZipPath = Join-Path $packageOutput $BundledPackageName
$stagedZipPath = Join-Path $packageStaging $PackageName
$stagedBundledZipPath = Join-Path $packageStaging $BundledPackageName

function Remove-SafeBuildDirectory {
    param([Parameter(Mandatory)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw '拒绝清理空路径。'
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $artifactsPrefix = $artifactsRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($artifactsPrefix, $pathComparison)) {
        throw "拒绝清理 artifacts 之外的目录：$fullPath"
    }
    if ($fullPath -eq $artifactsRoot -or
        $fullPath -eq [System.IO.Path]::GetPathRoot($fullPath)) {
        throw "拒绝清理构建根目录或文件系统根目录：$fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

function Test-IsToolsRelativePath {
    param([Parameter(Mandatory)][string]$RelativePath)

    return $RelativePath.Equals('tools', [System.StringComparison]::OrdinalIgnoreCase) -or
        $RelativePath.StartsWith('tools/', [System.StringComparison]::OrdinalIgnoreCase)
}

function Copy-DirectoryWithoutTools {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $Source -Force) {
        if ($item.Name.Equals('tools', [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $copyParameters = @{
            LiteralPath = $item.FullName
            Destination = $Destination
            Recurse = $true
            Force = $true
            ErrorAction = 'Stop'
        }
        Copy-Item @copyParameters
    }
}

function Install-ValidatedPackagePair {
    param(
        [Parameter(Mandatory)][string]$StagedLightweightPath,
        [Parameter(Mandatory)][string]$StagedBundledPath,
        [Parameter(Mandatory)][string]$FinalLightweightPath,
        [Parameter(Mandatory)][string]$FinalBundledPath
    )

    $previousLightweightPath = Join-Path $packageStaging '.previous-lightweight.zip'
    $previousBundledPath = Join-Path $packageStaging '.previous-bundled.zip'
    $lightweightBackedUp = $false
    $bundledBackedUp = $false
    $lightweightInstalled = $false
    $bundledInstalled = $false
    try {
        if (Test-Path -LiteralPath $FinalLightweightPath) {
            Move-Item -LiteralPath $FinalLightweightPath -Destination $previousLightweightPath
            $lightweightBackedUp = $true
        }
        if (Test-Path -LiteralPath $FinalBundledPath) {
            Move-Item -LiteralPath $FinalBundledPath -Destination $previousBundledPath
            $bundledBackedUp = $true
        }

        Move-Item -LiteralPath $StagedLightweightPath -Destination $FinalLightweightPath
        $lightweightInstalled = $true
        Move-Item -LiteralPath $StagedBundledPath -Destination $FinalBundledPath
        $bundledInstalled = $true

    }
    catch {
        if ($lightweightInstalled -and (Test-Path -LiteralPath $FinalLightweightPath)) {
            Remove-Item -LiteralPath $FinalLightweightPath -Force
        }
        if ($bundledInstalled -and (Test-Path -LiteralPath $FinalBundledPath)) {
            Remove-Item -LiteralPath $FinalBundledPath -Force
        }
        if ($lightweightBackedUp -and (Test-Path -LiteralPath $previousLightweightPath)) {
            Move-Item -LiteralPath $previousLightweightPath -Destination $FinalLightweightPath
        }
        if ($bundledBackedUp -and (Test-Path -LiteralPath $previousBundledPath)) {
            Move-Item -LiteralPath $previousBundledPath -Destination $FinalBundledPath
        }
        throw
    }

    foreach ($backupPath in @($previousLightweightPath, $previousBundledPath)) {
        if (Test-Path -LiteralPath $backupPath) {
            Remove-Item -LiteralPath $backupPath -Force
        }
    }
}

Remove-SafeBuildDirectory -Path $output
Remove-SafeBuildDirectory -Path $updaterOutput
Remove-SafeBuildDirectory -Path $lightweightStaging
Remove-SafeBuildDirectory -Path $packageStaging
New-Item -ItemType Directory -Path $output -Force | Out-Null
New-Item -ItemType Directory -Path $updaterOutput -Force | Out-Null
New-Item -ItemType Directory -Path $packageOutput -Force | Out-Null

$selfContainedValue = if ($SelfContained) { 'true' } else { 'false' }
$desktopPublishArguments = @(
    'publish'
    $desktopProject
    '-c'
    $Configuration
    '-r'
    $Runtime
    "--self-contained:$selfContainedValue"
    '-p:PublishSingleFile=false'
    '-p:DebugType=None'
    '-p:DebugSymbols=false'
    '-p:UsedAvaloniaProducts='
    '-p:UseSharedCompilation=false'
    "-p:Version=$AppVersion"
    "-p:InformationalVersion=$AppVersion"
    '-o'
    $output
)
& dotnet @desktopPublishArguments
$desktopPublishExitCode = $LASTEXITCODE
if ($desktopPublishExitCode -ne 0) {
    throw "Desktop dotnet publish failed with exit code $desktopPublishExitCode."
}

$updaterPublishArguments = @(
    'publish'
    $updaterProject
    '-c'
    $Configuration
    '-r'
    $Runtime
    '--self-contained:true'
    '-p:PublishSingleFile=true'
    '-p:PublishTrimmed=false'
    '-p:DebugType=None'
    '-p:DebugSymbols=false'
    '-p:UseSharedCompilation=false'
    "-p:Version=$AppVersion"
    "-p:InformationalVersion=$AppVersion"
    '-o'
    $updaterOutput
)
& dotnet @updaterPublishArguments
$updaterPublishExitCode = $LASTEXITCODE
if ($updaterPublishExitCode -ne 0) {
    throw "Updater dotnet publish failed with exit code $updaterPublishExitCode."
}

$publishedExecutable = Join-Path $output 'IGoLibrary.Ex.Desktop.exe'
$publishedUpdater = Join-Path $updaterOutput 'IGoLibrary.Ex.Updater.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published executable was not found: $publishedExecutable"
}
if (-not (Test-Path -LiteralPath $publishedUpdater -PathType Leaf)) {
    throw "Published updater was not found: $publishedUpdater"
}
Copy-Item -LiteralPath $publishedUpdater -Destination (Join-Path $output 'IGoLibrary.Ex.Updater.exe') -Force

$portableMarkerPath = Join-Path $output 'portable-release.marker'
[System.IO.File]::WriteAllText(
    $portableMarkerPath,
    'IGoLibrary-Ex|portable|win-x64|2',
    [System.Text.UTF8Encoding]::new($false))

$prepareCloudflared = Join-Path $PSScriptRoot 'prepare-cloudflared.ps1'
$cloudflaredDestination = Join-Path $output 'tools\cloudflared'
& $prepareCloudflared -Runtime $Runtime -DestinationDirectory $cloudflaredDestination

$manifestFiles = @(
    Get-ChildItem -LiteralPath $output -File -Recurse | ForEach-Object {
        $relativePath = [System.IO.Path]::GetRelativePath($output, $_.FullName).Replace('\', '/')
        if ($relativePath -ceq 'update-manifest.json' -or
            (Test-IsToolsRelativePath -RelativePath $relativePath)) {
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
    product = 'IGoLibrary-Ex'
    version = $AppVersion
    runtime = 'win-x64'
    entryExecutable = 'IGoLibrary.Ex.Desktop.exe'
    files = $manifestFiles
}
$manifestJson = $manifest | ConvertTo-Json -Depth 6
[System.IO.File]::WriteAllText(
    $manifestPath,
    $manifestJson,
    [System.Text.UTF8Encoding]::new($false))

try {
    Copy-DirectoryWithoutTools -Source $output -Destination $lightweightStaging
    New-Item -ItemType Directory -Path $packageStaging -Force | Out-Null

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $lightweightStaging,
        $stagedZipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $output,
        $stagedBundledZipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $false)

    $verifyScript = Join-Path $PSScriptRoot 'verify-windows-package.ps1'
    & $verifyScript -PackagePath $stagedZipPath -CompanionPackagePath $stagedBundledZipPath

    $installParameters = @{
        StagedLightweightPath = $stagedZipPath
        StagedBundledPath = $stagedBundledZipPath
        FinalLightweightPath = $zipPath
        FinalBundledPath = $bundledZipPath
    }
    Install-ValidatedPackagePair @installParameters
}
finally {
    Remove-SafeBuildDirectory -Path $lightweightStaging
    Remove-SafeBuildDirectory -Path $packageStaging
}

Write-Host "Published complete desktop tree to $output"
foreach ($package in @(
    [pscustomobject]@{ Label = 'Windows lightweight ZIP'; Path = $zipPath },
    [pscustomobject]@{ Label = 'Windows cloudflared ZIP'; Path = $bundledZipPath }
)) {
    $packageInfo = Get-Item -LiteralPath $package.Path
    $packageHash = (Get-FileHash -LiteralPath $package.Path -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "$($package.Label): $($package.Path)"
    Write-Host "  Size: $($packageInfo.Length) bytes"
    Write-Host "  SHA-256: $packageHash"
}
Write-Host '无后缀轻量包是唯一的应用内自动更新资产；首次启用自动更新仍需手动安装一次绿色版。'
