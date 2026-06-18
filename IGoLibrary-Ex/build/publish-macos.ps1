param(
    [string]$Configuration = "Release",
    [string]$Runtime = "osx-arm64",
    [string]$AppName = "IGoLibrary-Ex",
    [string]$BundleIdentifier = "com.igolibrary.ex",
    [string]$AppVersion = "1.0.0",
    [string]$PackageName,
    [string]$PublishOutput,
    [string]$IconSource,
    [switch]$SkipPublish
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$ExecutableName = "IGoLibrary.Ex.Desktop"
$IconFileName = "AppIcon.icns"
$Root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$Project = Join-Path $Root "src\IGoLibrary.Ex.Desktop\IGoLibrary.Ex.Desktop.csproj"
if ([string]::IsNullOrWhiteSpace($IconSource)) {
    $IconSource = Join-Path $Root "..\docs\images\ex\软件图标-大.png"
}
else {
    $IconSource = [System.IO.Path]::GetFullPath($IconSource)
}
if ([string]::IsNullOrWhiteSpace($PublishOutput)) {
    $PublishOutput = Join-Path $Root "artifacts\publish\$Runtime"
}
else {
    $PublishOutput = [System.IO.Path]::GetFullPath($PublishOutput)
}
$AppOutputRoot = Join-Path $Root "artifacts\macos\$Runtime"
$AppDir = Join-Path $AppOutputRoot "$AppName.app"
$ContentsDir = Join-Path $AppDir "Contents"
$MacOSDir = Join-Path $ContentsDir "MacOS"
$ResourcesDir = Join-Path $ContentsDir "Resources"
if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $PackageName = switch ($Runtime) {
        "osx-arm64" { "$AppName-macOS-Apple-Silicon-arm64.zip" }
        "osx-x64" { "$AppName-macOS-Intel-x64.zip" }
        default { "$AppName-$Runtime.zip" }
    }
}
$ZipPath = Join-Path $AppOutputRoot $PackageName
$FirstRunGuidePath = Join-Path $AppOutputRoot "macOS首次运行说明.txt"
$FirstRunCommandPath = Join-Path $AppOutputRoot "首次运行.command"

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
    $versionText = ConvertTo-PlistEscapedText $AppVersion
    $executableText = ConvertTo-PlistEscapedText $ExecutableName
    $iconFileText = ConvertTo-PlistEscapedText ([System.IO.Path]::GetFileNameWithoutExtension($IconFileName))
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
    <string>$versionText</string>
    <key>CFBundleShortVersionString</key>
    <string>$versionText</string>
    <key>CFBundleExecutable</key>
    <string>$executableText</string>
    <key>CFBundleIconFile</key>
    <string>$iconFileText</string>
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

function Copy-AppIcon {
    param(
        [string]$SourcePath,
        [string]$DestinationResourcesDir
    )

    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        Write-Warning "App icon source was not found: $SourcePath"
        return
    }

    $sips = Get-Command "sips" -ErrorAction SilentlyContinue
    $iconutil = Get-Command "iconutil" -ErrorAction SilentlyContinue
    if ($null -eq $sips -or $null -eq $iconutil) {
        Write-Warning "sips/iconutil was not found; skipping macOS app icon generation."
        return
    }

    $iconset = Join-Path $AppOutputRoot "AppIcon.iconset"
    if (Test-Path -LiteralPath $iconset) {
        Remove-Item -LiteralPath $iconset -Recurse -Force
    }

    New-Item -ItemType Directory -Path $iconset -Force | Out-Null
    foreach ($size in @(16, 32, 128, 256, 512)) {
        $oneXPath = Join-Path $iconset "icon_${size}x${size}.png"
        $twoXSize = $size * 2
        $twoXPath = Join-Path $iconset "icon_${size}x${size}@2x.png"
        & $sips.Source "-z" $size $size $SourcePath "--out" $oneXPath | Out-Null
        & $sips.Source "-z" $twoXSize $twoXSize $SourcePath "--out" $twoXPath | Out-Null
    }

    $destination = Join-Path $DestinationResourcesDir $IconFileName
    & $iconutil.Source "-c" "icns" $iconset "-o" $destination
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "iconutil rejected the generated iconset; writing ICNS container directly."
        Write-IcnsFromIconSet -IconSetPath $iconset -DestinationPath $destination
    }

    Remove-Item -LiteralPath $iconset -Recurse -Force
}

function Write-IcnsFromIconSet {
    param(
        [string]$IconSetPath,
        [string]$DestinationPath
    )

    $entries = @(
        @{ Type = "icp4"; Name = "icon_16x16.png" },
        @{ Type = "icp5"; Name = "icon_32x32.png" },
        @{ Type = "icp6"; Name = "icon_32x32@2x.png" },
        @{ Type = "ic07"; Name = "icon_128x128.png" },
        @{ Type = "ic08"; Name = "icon_256x256.png" },
        @{ Type = "ic09"; Name = "icon_512x512.png" },
        @{ Type = "ic10"; Name = "icon_512x512@2x.png" }
    )

    $chunks = New-Object System.Collections.Generic.List[byte[]]
    $payloadLength = 0
    foreach ($entry in $entries) {
        $path = Join-Path $IconSetPath $entry.Name
        $data = [System.IO.File]::ReadAllBytes($path)
        $chunk = New-Object byte[] ($data.Length + 8)
        [System.Text.Encoding]::ASCII.GetBytes($entry.Type, 0, 4, $chunk, 0) | Out-Null
        Write-BigEndianUInt32 -Buffer $chunk -Offset 4 -Value ($data.Length + 8)
        [Array]::Copy($data, 0, $chunk, 8, $data.Length)
        $chunks.Add($chunk)
        $payloadLength += $chunk.Length
    }

    $output = New-Object byte[] ($payloadLength + 8)
    [System.Text.Encoding]::ASCII.GetBytes("icns", 0, 4, $output, 0) | Out-Null
    Write-BigEndianUInt32 -Buffer $output -Offset 4 -Value ($payloadLength + 8)
    $offset = 8
    foreach ($chunk in $chunks) {
        [Array]::Copy($chunk, 0, $output, $offset, $chunk.Length)
        $offset += $chunk.Length
    }

    [System.IO.File]::WriteAllBytes($DestinationPath, $output)
}

function Write-BigEndianUInt32 {
    param(
        [byte[]]$Buffer,
        [int]$Offset,
        [int]$Value
    )

    $Buffer[$Offset] = [byte](($Value -shr 24) -band 0xff)
    $Buffer[$Offset + 1] = [byte](($Value -shr 16) -band 0xff)
    $Buffer[$Offset + 2] = [byte](($Value -shr 8) -band 0xff)
    $Buffer[$Offset + 3] = [byte]($Value -band 0xff)
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

if (-not $SkipPublish) {
    Write-Host "Publishing $Project for $Runtime..."
    dotnet publish $Project `
        -c $Configuration `
        -r $Runtime `
        --self-contained true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -p:UsedAvaloniaProducts= `
        -p:UseSharedCompilation=false `
        -o $PublishOutput

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}
else {
    Write-Host "Skipping dotnet publish; packaging existing files from $PublishOutput"
}

$publishedExecutable = Join-Path $PublishOutput $ExecutableName
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published executable was not found: $publishedExecutable"
}

if (Test-Path -LiteralPath $AppDir) {
    Remove-Item -LiteralPath $AppDir -Recurse -Force
}
if (Test-Path -LiteralPath $ZipPath) {
    Remove-Item -LiteralPath $ZipPath -Force
}

New-Item -ItemType Directory -Path $MacOSDir -Force | Out-Null
New-Item -ItemType Directory -Path $ResourcesDir -Force | Out-Null
Get-ChildItem -LiteralPath $PublishOutput -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $MacOSDir -Recurse -Force
}
Copy-AppIcon -SourcePath $IconSource -DestinationResourcesDir $ResourcesDir
Write-InfoPlist (Join-Path $ContentsDir "Info.plist")
Write-FirstRunGuide $FirstRunGuidePath
Write-FirstRunCommand $FirstRunCommandPath
New-MacAppZip -SourceAppDir $AppDir -DestinationZip $ZipPath -AdditionalFiles @($FirstRunGuidePath, $FirstRunCommandPath)

Write-Host "Published files to $PublishOutput"
Write-Host "Created macOS app bundle at $AppDir"
Write-Host "Created permission-preserving macOS zip at $ZipPath"
Write-Host "Unsigned builds may require users to remove quarantine on first run. See macOS first-run instructions inside the zip."
