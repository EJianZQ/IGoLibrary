param(
    [string]$Configuration = "Release",
    [ValidateSet('osx-arm64', 'osx-x64')]
    [string]$Runtime = "osx-arm64",
    [string]$AppName = "IGoLibrary-Ex",
    [string]$BundleIdentifier = "com.igolibrary.ex",
    [string]$AppVersion,
    [string]$PackageName,
    [string]$BundledPackageName,
    [string]$PublishOutput,
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
$BundleVersion = $AppVersion

$ExecutableName = "IGoLibrary.Ex.Desktop"
$Root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$ArtifactsRoot = [System.IO.Path]::GetFullPath((Join-Path $Root 'artifacts'))
$PathComparison = if ([System.OperatingSystem]::IsWindows()) {
    [System.StringComparison]::OrdinalIgnoreCase
}
else {
    [System.StringComparison]::Ordinal
}
$Project = Join-Path $Root "src\IGoLibrary.Ex.Desktop\IGoLibrary.Ex.Desktop.csproj"
if ([string]::IsNullOrWhiteSpace($PublishOutput)) {
    $UsesDefaultPublishOutput = $true
    $PublishOutput = Join-Path $Root "artifacts\publish\$Runtime"
}
else {
    $UsesDefaultPublishOutput = $false
    $PublishOutput = [System.IO.Path]::GetFullPath($PublishOutput)
}
$PublishOutput = [System.IO.Path]::GetFullPath($PublishOutput)
if ([string]::IsNullOrWhiteSpace($AppName) -or
    $AppName.Contains('/') -or
    $AppName.Contains('\') -or
    $AppName -in @('.', '..') -or
    [System.IO.Path]::GetFileName($AppName) -cne $AppName) {
    throw "macOS 应用名必须是不含目录的叶名称：$AppName"
}
$AppOutputRoot = Join-Path $Root "artifacts\macos\$Runtime"
$AppDir = Join-Path $AppOutputRoot "$AppName.app"
$ContentsDir = Join-Path $AppDir "Contents"
$MacOSDir = Join-Path $ContentsDir "MacOS"
$ResourcesDir = Join-Path $ContentsDir "Resources"
if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $PackageName = switch ($Runtime) {
        "osx-arm64" { "$AppName-v$AppVersion-macOS-Apple-Silicon-arm64.zip" }
        "osx-x64" { "$AppName-v$AppVersion-macOS-Intel-x64.zip" }
        default { "$AppName-v$AppVersion-$Runtime.zip" }
    }
}
if ([string]::IsNullOrWhiteSpace($BundledPackageName)) {
    $BundledPackageName = switch ($Runtime) {
        "osx-arm64" { "$AppName-v$AppVersion-macOS-Apple-Silicon-arm64-with-cloudflared.zip" }
        "osx-x64" { "$AppName-v$AppVersion-macOS-Intel-x64-with-cloudflared.zip" }
    }
}
foreach ($candidatePackageName in @($PackageName, $BundledPackageName)) {
    if ([string]::IsNullOrWhiteSpace($candidatePackageName) -or
        $candidatePackageName.Contains('/') -or
        $candidatePackageName.Contains('\') -or
        -not $candidatePackageName.EndsWith('.zip', [System.StringComparison]::OrdinalIgnoreCase) -or
        [System.IO.Path]::GetFileName($candidatePackageName) -cne $candidatePackageName) {
        throw "macOS 包名必须是不含目录的 .zip 文件名：$candidatePackageName"
    }
}
if ($PackageName -ieq $BundledPackageName) {
    throw '轻量包和 cloudflared 完整包不能使用同一个文件名。'
}
$ZipPath = Join-Path $AppOutputRoot $PackageName
$BundledZipPath = Join-Path $AppOutputRoot $BundledPackageName
$PackageStaging = [System.IO.Path]::GetFullPath((Join-Path $ArtifactsRoot "staging\macos\$Runtime\packages"))
$StagedZipPath = Join-Path $PackageStaging $PackageName
$StagedBundledZipPath = Join-Path $PackageStaging $BundledPackageName
$FirstRunGuidePath = Join-Path $AppOutputRoot "macOS首次运行说明.txt"
$FirstRunCommandPath = Join-Path $AppOutputRoot "首次运行.command"

function Remove-SafeArtifactDirectory {
    param([Parameter(Mandatory)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        throw '拒绝清理空路径。'
    }

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $artifactsPrefix = $ArtifactsRoot.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($artifactsPrefix, $PathComparison) -or
        $fullPath -eq $ArtifactsRoot -or
        $fullPath -eq [System.IO.Path]::GetPathRoot($fullPath)) {
        throw "拒绝清理 artifacts 之外或构建根级目录：$fullPath"
    }
    if (Test-Path -LiteralPath $fullPath) {
        Remove-Item -LiteralPath $fullPath -Recurse -Force
    }
}

function ConvertTo-PlistEscapedText {
    param([string]$Value)

    return $Value.
        Replace("&", "&amp;").
        Replace("<", "&lt;").
        Replace(">", "&gt;").
        Replace('"', "&quot;").
        Replace("'", "&apos;")
}

function Write-InfoPlist {
    param([string]$Path)

    $bundleName = ConvertTo-PlistEscapedText $AppName
    $bundleIdentifierText = ConvertTo-PlistEscapedText $BundleIdentifier
    $bundleVersionText = ConvertTo-PlistEscapedText $BundleVersion
    $executableText = ConvertTo-PlistEscapedText $ExecutableName
    $content = @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
  <dict>
    <key>CFBundleName</key>
    <string>$bundleName</string>
    <key>CFBundleDisplayName</key>
    <string>$bundleName</string>
    <key>CFBundleIdentifier</key>
    <string>$bundleIdentifierText</string>
    <key>CFBundleVersion</key>
    <string>$bundleVersionText</string>
    <key>CFBundleShortVersionString</key>
    <string>$bundleVersionText</string>
    <key>CFBundleExecutable</key>
    <string>$executableText</string>
    <key>CFBundlePackageType</key>
    <string>APPL</string>
    <key>LSMinimumSystemVersion</key>
    <string>12.0</string>
    <key>NSHighResolutionCapable</key>
    <true/>
  </dict>
</plist>
"@

    [System.IO.File]::WriteAllText($Path, $content, [System.Text.UTF8Encoding]::new($false))
}

function Write-FirstRunGuide {
    param([string]$Path)

    $content = @"
IGoLibrary-Ex macOS 首次运行说明

此版本未签名、未公证。macOS 首次运行时可能提示“已损坏，无法打开”。
这通常是 Gatekeeper 对下载文件添加了隔离标记，不代表压缩包真的损坏。

推荐操作：
1. 解压 zip，确认本文件和 $AppName.app 在同一个目录。
2. 打开“终端”。
3. 输入下面这行命令，注意末尾保留一个空格：

   xattr -dr com.apple.quarantine 

4. 把 $AppName.app 从 Finder 拖到终端窗口里，终端会自动补全路径。
5. 按回车执行。
6. 再双击或右键打开 $AppName.app。

如果你愿意，也可以尝试双击同目录下的“首次运行.command”，它会自动执行解除隔离并打开应用。
"@

    [System.IO.File]::WriteAllText($Path, $content, [System.Text.UTF8Encoding]::new($false))
}

function Write-FirstRunCommand {
    param([string]$Path)

    $content = @'
#!/bin/bash
set -e

APP_PATH="$(cd "$(dirname "$0")" && pwd)/__APP_NAME__.app"

if [[ ! -d "$APP_PATH" ]]; then
  echo "Cannot find __APP_NAME__.app next to this script."
  read -r -p "Press Enter to close..."
  exit 1
fi

xattr -dr com.apple.quarantine "$APP_PATH" 2>/dev/null || true
open "$APP_PATH"
'@.Replace("__APP_NAME__", $AppName)

    [System.IO.File]::WriteAllText($Path, $content, [System.Text.UTF8Encoding]::new($false))
}

function Get-ZipEntryMode {
    param(
        [System.IO.FileSystemInfo]$Item,
        [string]$EntryName
    )

    if ($Item.PSIsContainer) {
        return [Convert]::ToInt32("40755", 8)
    }

    $leafName = $Item.Name
    $executableEntryName = "$AppName.app/Contents/MacOS/$ExecutableName"
    if ($EntryName -eq $executableEntryName -or
        $leafName -eq "cloudflared" -or
        $leafName -eq "createdump" -or
        $leafName.EndsWith(".dylib", [StringComparison]::OrdinalIgnoreCase) -or
        $leafName.EndsWith(".command", [StringComparison]::OrdinalIgnoreCase)) {
        return [Convert]::ToInt32("100755", 8)
    }

    return [Convert]::ToInt32("100644", 8)
}

function Add-ZipEntry {
    param(
        [System.IO.Compression.ZipArchive]$Archive,
        [System.IO.FileSystemInfo]$Item,
        [string]$EntryName
    )

    $normalizedEntryName = $EntryName.Replace("\", "/")
    if ($Item.PSIsContainer -and -not $normalizedEntryName.EndsWith("/", [StringComparison]::Ordinal)) {
        $normalizedEntryName += "/"
    }

    $entry = $Archive.CreateEntry($normalizedEntryName, [System.IO.Compression.CompressionLevel]::Optimal)
    $entry.LastWriteTime = [DateTimeOffset]$Item.LastWriteTime
    $entry.ExternalAttributes = (Get-ZipEntryMode $Item $normalizedEntryName.TrimEnd("/")) -shl 16

    if ($Item.PSIsContainer) {
        return
    }

    $entryStream = $entry.Open()
    try {
        $fileStream = [System.IO.File]::OpenRead($Item.FullName)
        try {
            $fileStream.CopyTo($entryStream)
        }
        finally {
            $fileStream.Dispose()
        }
    }
    finally {
        $entryStream.Dispose()
    }
}

function Set-ZipCentralDirectoryHostToUnix {
    param([string]$Path)

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $eocdOffset = -1
    for ($index = $bytes.Length - 22; $index -ge 0; $index--) {
        if ($bytes[$index] -eq 0x50 -and
            $bytes[$index + 1] -eq 0x4b -and
            $bytes[$index + 2] -eq 0x05 -and
            $bytes[$index + 3] -eq 0x06) {
            $eocdOffset = $index
            break
        }
    }

    if ($eocdOffset -lt 0) {
        throw "End of central directory was not found in $Path."
    }

    $entryCount = [BitConverter]::ToUInt16($bytes, $eocdOffset + 10)
    $centralDirectoryOffset = [BitConverter]::ToUInt32($bytes, $eocdOffset + 16)
    $position = [int]$centralDirectoryOffset

    for ($entryIndex = 0; $entryIndex -lt $entryCount; $entryIndex++) {
        if ($bytes[$position] -ne 0x50 -or
            $bytes[$position + 1] -ne 0x4b -or
            $bytes[$position + 2] -ne 0x01 -or
            $bytes[$position + 3] -ne 0x02) {
            throw "Central directory entry $entryIndex was not found in $Path."
        }

        $bytes[$position + 5] = 3
        $fileNameLength = [BitConverter]::ToUInt16($bytes, $position + 28)
        $extraLength = [BitConverter]::ToUInt16($bytes, $position + 30)
        $commentLength = [BitConverter]::ToUInt16($bytes, $position + 32)
        $position += 46 + $fileNameLength + $extraLength + $commentLength
    }

    [System.IO.File]::WriteAllBytes($Path, $bytes)
}

function New-MacAppZip {
    param(
        [string]$SourceAppDir,
        [string]$DestinationZip,
        [string[]]$AdditionalFiles = @()
    )

    if (Test-Path -LiteralPath $DestinationZip) {
        Remove-Item -LiteralPath $DestinationZip -Force
    }

    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::Open($DestinationZip, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $rootItem = Get-Item -LiteralPath $SourceAppDir -Force
        Add-ZipEntry $archive $rootItem "$AppName.app"

        $basePath = $rootItem.FullName
        $items = Get-ChildItem -LiteralPath $basePath -Recurse -Force |
            Sort-Object @{ Expression = { -not $_.PSIsContainer } }, FullName

        foreach ($item in $items) {
            $trimChars = [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
            $relative = $item.FullName.Substring($basePath.Length).TrimStart($trimChars).Replace("\", "/")
            Add-ZipEntry $archive $item "$AppName.app/$relative"
        }

        foreach ($file in $AdditionalFiles) {
            $fileItem = Get-Item -LiteralPath $file -Force
            Add-ZipEntry $archive $fileItem $fileItem.Name
        }
    }
    finally {
        $archive.Dispose()
    }

    Set-ZipCentralDirectoryHostToUnix $DestinationZip
}

function New-MacAppBundle {
    param([Parameter(Mandatory)][bool]$IncludeTools)

    Remove-SafeArtifactDirectory -Path $AppDir
    New-Item -ItemType Directory -Path $MacOSDir -Force | Out-Null
    New-Item -ItemType Directory -Path $ResourcesDir -Force | Out-Null
    foreach ($item in Get-ChildItem -LiteralPath $PublishOutput -Force) {
        if ($item.Name.Equals('tools', [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $copyParameters = @{
            LiteralPath = $item.FullName
            Destination = $MacOSDir
            Recurse = $true
            Force = $true
            ErrorAction = 'Stop'
        }
        Copy-Item @copyParameters
    }
    if ($IncludeTools) {
        $cloudflaredSource = Join-Path $PublishOutput 'tools\cloudflared'
        $cloudflaredTarget = Join-Path $MacOSDir 'tools\cloudflared'
        New-Item -ItemType Directory -Path $cloudflaredTarget -Force | Out-Null
        $cloudflaredFileName = 'cloudflared'
        foreach ($fileName in @($cloudflaredFileName, 'LICENSE.txt', 'THIRD-PARTY-NOTICES.txt')) {
            $sourcePath = Join-Path $cloudflaredSource $fileName
            if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
                throw "完整包缺少必需的 cloudflared 文件：$sourcePath"
            }
            Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $cloudflaredTarget $fileName) -Force
        }
    }
    Write-InfoPlist (Join-Path $ContentsDir 'Info.plist')
}

function Get-ZipEntrySha256 {
    param([Parameter(Mandatory)][System.IO.Compression.ZipArchiveEntry]$Entry)

    $sha = [System.Security.Cryptography.SHA256]::Create()
    $stream = $Entry.Open()
    try {
        return [System.Convert]::ToHexString($sha.ComputeHash($stream)).ToLowerInvariant()
    }
    finally {
        $stream.Dispose()
        $sha.Dispose()
    }
}

function Test-MacAppZip {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][bool]$IncludeTools
    )

    $resolvedPath = (Resolve-Path -LiteralPath $Path).Path
    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPath)
    try {
        $fileEntries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
        $entryByPath = [System.Collections.Generic.Dictionary[string, System.IO.Compression.ZipArchiveEntry]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        foreach ($entry in $fileEntries) {
            if (-not $entryByPath.TryAdd($entry.FullName, $entry)) {
                throw "macOS ZIP 包含大小写重复路径：$($entry.FullName)"
            }
        }

        $appPrefix = "$AppName.app/"
        $guideName = [System.IO.Path]::GetFileName($FirstRunGuidePath)
        $commandName = [System.IO.Path]::GetFileName($FirstRunCommandPath)
        foreach ($entry in $fileEntries) {
            if (-not $entry.FullName.StartsWith($appPrefix, [System.StringComparison]::Ordinal) -and
                $entry.FullName -cne $guideName -and
                $entry.FullName -cne $commandName) {
                throw "macOS ZIP 包含应用目录之外的意外文件：$($entry.FullName)"
            }
        }

        $mainExecutablePath = $appPrefix + "Contents/MacOS/$ExecutableName"
        foreach ($requiredPath in @($mainExecutablePath, $guideName, $commandName)) {
            if (-not $entryByPath.ContainsKey($requiredPath)) {
                throw "macOS ZIP 缺少必需文件：$requiredPath"
            }
        }

        $toolsPrefix = $appPrefix + 'Contents/MacOS/tools/'
        $actualToolPaths = @(
            $fileEntries |
                Where-Object {
                    $_.FullName.StartsWith($toolsPrefix, [System.StringComparison]::OrdinalIgnoreCase)
                } |
                ForEach-Object FullName
        )
        $expectedToolPaths = if ($IncludeTools) {
            @(
                ($toolsPrefix + 'cloudflared/cloudflared')
                ($toolsPrefix + 'cloudflared/LICENSE.txt')
                ($toolsPrefix + 'cloudflared/THIRD-PARTY-NOTICES.txt')
            )
        }
        else {
            @()
        }
        $actualToolSet = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        foreach ($toolPath in $actualToolPaths) {
            $null = $actualToolSet.Add($toolPath)
        }
        $expectedToolSet = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        foreach ($toolPath in $expectedToolPaths) {
            $null = $expectedToolSet.Add($toolPath)
        }
        if (-not $actualToolSet.SetEquals($expectedToolSet)) {
            $variant = if ($IncludeTools) { '完整包' } else { '轻量包' }
            throw "$variant 的 tools 文件集合无效。实际：$($actualToolPaths -join ', ')"
        }

        if ($IncludeTools) {
            $cloudflaredPath = $toolsPrefix + 'cloudflared/cloudflared'
            $actualCloudflaredHash = Get-ZipEntrySha256 -Entry $entryByPath[$cloudflaredPath]
            $preparedCloudflaredPath = Join-Path $PublishOutput 'tools\cloudflared\cloudflared'
            $preparedCloudflaredHash = Get-FileHash -LiteralPath $preparedCloudflaredPath -Algorithm SHA256
            $expectedCloudflaredHash = $preparedCloudflaredHash.Hash.ToLowerInvariant()
            if ($actualCloudflaredHash -cne $expectedCloudflaredHash) {
                throw "macOS cloudflared SHA-256 无效：$actualCloudflaredHash"
            }

            foreach ($comparison in @(
                [pscustomobject]@{
                    EntryPath = $toolsPrefix + 'cloudflared/LICENSE.txt'
                    SourcePath = Join-Path $PSScriptRoot 'third-party\cloudflared-LICENSE.txt'
                },
                [pscustomobject]@{
                    EntryPath = $toolsPrefix + 'cloudflared/THIRD-PARTY-NOTICES.txt'
                    SourcePath = Join-Path $PSScriptRoot 'third-party\THIRD-PARTY-NOTICES.txt'
                }
            )) {
                $entryHash = Get-ZipEntrySha256 -Entry $entryByPath[$comparison.EntryPath]
                $sourceHash = (Get-FileHash -LiteralPath $comparison.SourcePath -Algorithm SHA256).Hash.ToLowerInvariant()
                if ($entryHash -cne $sourceHash) {
                    throw "macOS ZIP 文件与仓库源文件不一致：$($comparison.EntryPath)"
                }
            }
        }

        $expectedExecutableMode = [System.Convert]::ToInt32('100755', 8)
        $executableEntries = @(
            $fileEntries | Where-Object {
                $_.FullName -ceq $mainExecutablePath -or
                $_.FullName -ceq $commandName -or
                ($_.FullName.StartsWith(
                        $appPrefix + 'Contents/MacOS/',
                        [System.StringComparison]::Ordinal) -and
                 ($_.Name -ceq 'createdump' -or
                  $_.Name -ceq 'cloudflared' -or
                  $_.Name.EndsWith('.dylib', [System.StringComparison]::OrdinalIgnoreCase)))
            }
        )
        foreach ($entry in $executableEntries) {
            $actualMode = ($entry.ExternalAttributes -shr 16) -band 0xFFFF
            if ($actualMode -ne $expectedExecutableMode) {
                throw "macOS ZIP 可执行权限无效：$($entry.FullName)，mode=$actualMode"
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Install-ValidatedPackagePair {
    param(
        [Parameter(Mandatory)][string]$StagedLightweightPath,
        [Parameter(Mandatory)][string]$StagedBundledPath,
        [Parameter(Mandatory)][string]$FinalLightweightPath,
        [Parameter(Mandatory)][string]$FinalBundledPath
    )

    $previousLightweightPath = Join-Path $PackageStaging '.previous-lightweight.zip'
    $previousBundledPath = Join-Path $PackageStaging '.previous-bundled.zip'
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

if (-not $SkipPublish) {
    if ($UsesDefaultPublishOutput) {
        Remove-SafeArtifactDirectory -Path $PublishOutput
    }
    Write-Host "Publishing $Project for $Runtime..."
    $publishArguments = @(
        'publish'
        $Project
        '-c'
        $Configuration
        '-r'
        $Runtime
        '--self-contained'
        'true'
        '-p:DebugType=None'
        '-p:DebugSymbols=false'
        '-p:UsedAvaloniaProducts='
        '-p:UseSharedCompilation=false'
        "-p:Version=$AppVersion"
        "-p:InformationalVersion=$AppVersion"
        '-o'
        $PublishOutput
    )
    & dotnet @publishArguments
    $publishExitCode = $LASTEXITCODE
    if ($publishExitCode -ne 0) {
        throw "dotnet publish failed with exit code $publishExitCode."
    }
}
else {
    Write-Host "Skipping dotnet publish; packaging existing files from $PublishOutput"
}

$publishedExecutable = Join-Path $PublishOutput $ExecutableName
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published executable was not found: $publishedExecutable"
}

$prepareCloudflared = Join-Path $PSScriptRoot 'prepare-cloudflared.ps1'
$cloudflaredDestination = Join-Path $PublishOutput 'tools\cloudflared'
& $prepareCloudflared -Runtime $Runtime -DestinationDirectory $cloudflaredDestination

New-Item -ItemType Directory -Path $AppOutputRoot -Force | Out-Null
Write-FirstRunGuide $FirstRunGuidePath
Write-FirstRunCommand $FirstRunCommandPath

Remove-SafeArtifactDirectory -Path $PackageStaging
New-Item -ItemType Directory -Path $PackageStaging -Force | Out-Null
try {
    New-MacAppBundle -IncludeTools $false
    New-MacAppZip -SourceAppDir $AppDir -DestinationZip $StagedZipPath -AdditionalFiles @($FirstRunGuidePath, $FirstRunCommandPath)

    New-MacAppBundle -IncludeTools $true
    New-MacAppZip -SourceAppDir $AppDir -DestinationZip $StagedBundledZipPath -AdditionalFiles @($FirstRunGuidePath, $FirstRunCommandPath)

    Test-MacAppZip -Path $StagedZipPath -IncludeTools $false
    Test-MacAppZip -Path $StagedBundledZipPath -IncludeTools $true

    $installParameters = @{
        StagedLightweightPath = $StagedZipPath
        StagedBundledPath = $StagedBundledZipPath
        FinalLightweightPath = $ZipPath
        FinalBundledPath = $BundledZipPath
    }
    Install-ValidatedPackagePair @installParameters
}
finally {
    Remove-SafeArtifactDirectory -Path $PackageStaging
}

Write-Host "Published files to $PublishOutput"
Write-Host "Created macOS app bundle at $AppDir"
foreach ($package in @(
    [pscustomobject]@{ Label = 'macOS lightweight ZIP'; Path = $ZipPath },
    [pscustomobject]@{ Label = 'macOS cloudflared ZIP'; Path = $BundledZipPath }
)) {
    $packageInfo = Get-Item -LiteralPath $package.Path
    $packageHash = (Get-FileHash -LiteralPath $package.Path -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "$($package.Label): $($package.Path)"
    Write-Host "  Size: $($packageInfo.Length) bytes"
    Write-Host "  SHA-256: $packageHash"
}
Write-Host "Unsigned builds may require users to remove quarantine on first run. See macOS first-run instructions inside the zip."
