param(
    [Parameter(Mandatory)]
    [string]$PackagePath,

    [Parameter(Mandatory)]
    [string]$RawPackagePath,

    [Parameter(Mandatory)]
    [string]$CompanionPackagePath,

    [Parameter(Mandatory)]
    [string]$CompanionRawPackagePath,

    [Parameter(Mandatory)]
    [string]$PublishedLauncherPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression.FileSystem

$maximumLauncherBytes = 10MB
$maximumLauncherCompressedBytes = 5MB
$portableRoot = 'IGoLibrary-Ex'
$launcherEntryPath = 'IGoLibrary-Ex/IGoLibrary-Ex.exe'
$appEntryPrefix = 'IGoLibrary-Ex/app/'

function Test-IsSymbolicLinkOrReparsePoint {
    param([Parameter(Mandatory)][System.IO.Compression.ZipArchiveEntry]$Entry)

    $unixFileType = ($Entry.ExternalAttributes -shr 16) -band 0xF000
    $dosAttributes = $Entry.ExternalAttributes -band 0xFFFF
    return $unixFileType -eq 0xA000 -or ($dosAttributes -band 0x0400) -ne 0
}

function Test-IsReservedWindowsComponent {
    param([Parameter(Mandatory)][string]$Component)

    $baseName = $Component.Split('.')[0]
    return $baseName -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$'
}

function Assert-SafeArchivePath {
    param(
        [Parameter(Mandatory)][System.IO.Compression.ZipArchiveEntry]$Entry,
        [Parameter(Mandatory)][string]$Label
    )

    $rawPath = $Entry.FullName
    $path = $rawPath.TrimEnd('/', '\')
    if ([string]::IsNullOrWhiteSpace($path) -or
        $rawPath.Contains('\') -or
        $path.StartsWith('/', [System.StringComparison]::Ordinal) -or
        $path -match '^[A-Za-z]:' -or
        (Test-IsSymbolicLinkOrReparsePoint -Entry $Entry)) {
        throw "$Label 包含非法路径或链接条目：$rawPath"
    }

    $invalidFileNameCharacters = [System.IO.Path]::GetInvalidFileNameChars()
    foreach ($component in $path.Split('/')) {
        if ([string]::IsNullOrEmpty($component) -or
            $component -ceq '.' -or
            $component -ceq '..' -or
            $component.EndsWith('.', [System.StringComparison]::Ordinal) -or
            $component.EndsWith(' ', [System.StringComparison]::Ordinal) -or
            $component.IndexOfAny($invalidFileNameCharacters) -ge 0 -or
            (Test-IsReservedWindowsComponent -Component $component)) {
            throw "$Label 包含 Windows 不安全路径组件：$rawPath"
        }
    }
}

function Get-ArchiveIndex {
    param(
        [Parameter(Mandatory)][System.IO.Compression.ZipArchive]$Archive,
        [Parameter(Mandatory)][string]$Label
    )

    $pathSet = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $files = [System.Collections.Generic.Dictionary[string, System.IO.Compression.ZipArchiveEntry]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $directories = [System.Collections.Generic.Dictionary[string, System.IO.Compression.ZipArchiveEntry]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)

    foreach ($entry in $Archive.Entries) {
        Assert-SafeArchivePath -Entry $entry -Label $Label
        $path = $entry.FullName.TrimEnd('/', '\')
        if (-not $pathSet.Add($path)) {
            throw "$Label 包含重复或大小写冲突路径：$path"
        }

        $isDirectory = $entry.FullName.EndsWith('/') -or $entry.FullName.EndsWith('\')
        if ($isDirectory) {
            $directories.Add($path, $entry)
        }
        else {
            $files.Add($path, $entry)
        }
    }

    return [pscustomobject]@{
        Files = $files
        Directories = $directories
    }
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

function Get-ZipEntryVersionInfo {
    param([Parameter(Mandatory)][System.IO.Compression.ZipArchiveEntry]$Entry)

    $temporaryPath = Join-Path (
        [System.IO.Path]::GetTempPath()) (
        "IGoLibrary-Ex-launcher-version-$([Guid]::NewGuid().ToString('N')).exe")
    try {
        $source = $Entry.Open()
        try {
            $destination = [System.IO.FileStream]::new(
                $temporaryPath,
                [System.IO.FileMode]::CreateNew,
                [System.IO.FileAccess]::Write,
                [System.IO.FileShare]::None)
            try {
                $source.CopyTo($destination)
            }
            finally {
                $destination.Dispose()
            }
        }
        finally {
            $source.Dispose()
        }

        $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($temporaryPath)
        return [pscustomobject]@{
            FileVersion = $versionInfo.FileVersion
            ProductVersion = $versionInfo.ProductVersion
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
}

function Assert-NativeLauncherPe {
    param([Parameter(Mandatory)][System.IO.Compression.ZipArchiveEntry]$Entry)

    $entryStream = $Entry.Open()
    try {
        $stream = [System.IO.MemoryStream]::new()
        try {
            $entryStream.CopyTo($stream)
            $stream.Position = 0
            $reader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
            try {
                if ([string]$reader.PEHeaders.CoffHeader.Machine -cne 'Amd64') {
                    throw '外层启动器必须是 AMD64 PE。'
                }
                if ([string]$reader.PEHeaders.PEHeader.Subsystem -cne 'WindowsGui') {
                    throw '外层启动器必须使用 Windows GUI 子系统。'
                }
                if ($null -ne $reader.PEHeaders.CorHeader) {
                    throw '外层启动器包含 CLR 入口，不是纯 Native AOT 可执行文件。'
                }
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $entryStream.Dispose()
    }
}

function Assert-EntryMatchesFile {
    param(
        [Parameter(Mandatory)][System.IO.Compression.ZipArchiveEntry]$Entry,
        [Parameter(Mandatory)][string]$FilePath
    )

    $source = Get-Item -LiteralPath $FilePath
    if ($Entry.Length -ne $source.Length) {
        throw 'portable ZIP 中的启动器长度与已发布启动器不一致。'
    }

    $entryHash = Get-ZipEntrySha256 -Entry $Entry
    $sourceHash = (Get-FileHash -LiteralPath $source.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($entryHash -cne $sourceHash) {
        throw 'portable ZIP 中的启动器与已发布启动器内容不一致。'
    }

    return $entryHash
}

function Assert-AppMatchesRawPackage {
    param(
        [Parameter(Mandatory)]$PortableIndex,
        [Parameter(Mandatory)]$RawIndex,
        [Parameter(Mandatory)][string]$Label
    )

    if ($PortableIndex.Files.Count -ne $RawIndex.Files.Count + 1) {
        throw "$Label 的文件数量与原始更新载荷不一致。"
    }

    foreach ($portablePath in $PortableIndex.Files.Keys) {
        if ($portablePath -ceq $launcherEntryPath) {
            continue
        }
        if (-not $portablePath.StartsWith(
                $appEntryPrefix,
                [System.StringComparison]::Ordinal)) {
            throw "$Label 在外层目录包含非启动器文件：$portablePath"
        }

        $relativePath = $portablePath.Substring($appEntryPrefix.Length)
        if ([string]::IsNullOrWhiteSpace($relativePath) -or
            -not $RawIndex.Files.ContainsKey($relativePath)) {
            throw "$Label 的 app 包含原始更新载荷之外的文件：$portablePath"
        }
    }

    foreach ($rawPath in $RawIndex.Files.Keys) {
        $portablePath = $appEntryPrefix + $rawPath
        if (-not $PortableIndex.Files.ContainsKey($portablePath)) {
            throw "$Label 的 app 缺少原始更新载荷文件：$portablePath"
        }

        $rawEntry = $RawIndex.Files[$rawPath]
        $portableEntry = $PortableIndex.Files[$portablePath]
        if ($rawEntry.FullName -cne $rawPath -or
            $portableEntry.FullName -cne $portablePath) {
            throw "$Label 的 app 文件大小写与原始更新载荷不一致：$portablePath"
        }
        if ($rawEntry.Length -ne $portableEntry.Length) {
            throw "$Label 的 app 文件长度与原始更新载荷不一致：$portablePath"
        }

        $rawHash = Get-ZipEntrySha256 -Entry $rawEntry
        $portableHash = Get-ZipEntrySha256 -Entry $portableEntry
        if ($rawHash -cne $portableHash) {
            throw "$Label 的 app 文件内容与原始更新载荷不一致：$portablePath"
        }
    }

    $mappedPortableDirectories = [System.Collections.Generic.Dictionary[string, string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($portableDirectory in $PortableIndex.Directories.Keys) {
        if ($portableDirectory -ceq $portableRoot -or
            $portableDirectory -ceq "$portableRoot/app") {
            continue
        }
        if (-not $portableDirectory.StartsWith(
                $appEntryPrefix,
                [System.StringComparison]::Ordinal)) {
            throw "$Label 包含 app 之外的额外目录：$portableDirectory"
        }

        $relativePath = $portableDirectory.Substring($appEntryPrefix.Length)
        if ($mappedPortableDirectories.ContainsKey($relativePath)) {
            throw "$Label 包含重复 app 目录：$portableDirectory"
        }
        $mappedPortableDirectories.Add($relativePath, $portableDirectory)
    }

    $rawDirectories = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($rawDirectory in $RawIndex.Directories.Keys) {
        $null = $rawDirectories.Add($rawDirectory)
    }
    $mappedPortableDirectorySet = [System.Collections.Generic.HashSet[string]]::new(
        $mappedPortableDirectories.Keys,
        [System.StringComparer]::OrdinalIgnoreCase)
    if (-not $mappedPortableDirectorySet.SetEquals($rawDirectories)) {
        throw "$Label 的 app 目录集合与原始更新载荷不一致。"
    }
    foreach ($rawDirectory in $RawIndex.Directories.Keys) {
        $expectedPortableDirectory = $appEntryPrefix + $rawDirectory
        if ($RawIndex.Directories[$rawDirectory].FullName.TrimEnd('/', '\') -cne $rawDirectory -or
            $mappedPortableDirectories[$rawDirectory] -cne $expectedPortableDirectory) {
            throw "$Label 的 app 目录大小写与原始更新载荷不一致：$expectedPortableDirectory"
        }
    }
}

function Test-PortablePackage {
    param(
        [Parameter(Mandatory)][string]$PortablePath,
        [Parameter(Mandatory)][string]$RawPath,
        [Parameter(Mandatory)][string]$LauncherPath
    )

    $resolvedPortable = (Resolve-Path -LiteralPath $PortablePath).Path
    $resolvedRaw = (Resolve-Path -LiteralPath $RawPath).Path
    $resolvedLauncher = (Resolve-Path -LiteralPath $LauncherPath).Path
    $portableName = [System.IO.Path]::GetFileName($resolvedPortable)
    $rawName = [System.IO.Path]::GetFileName($resolvedRaw)
    $defaultPattern = '^IGoLibrary-Ex-v(?<version>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))-windows-x64-portable\.zip$'
    $lightweightPattern = '^IGoLibrary-Ex-v(?<version>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))-windows-x64-portable-without-cloudflared\.zip$'
    if ($portableName -match $defaultPattern) {
        $variant = 'Default'
        $version = $Matches.version
        $expectedRawName = "IGoLibrary-Ex-v$version-windows-x64.zip"
    }
    elseif ($portableName -match $lightweightPattern) {
        $variant = 'Lightweight'
        $version = $Matches.version
        $expectedRawName = "IGoLibrary-Ex-v$version-windows-x64-without-cloudflared.zip"
    }
    else {
        throw "Windows portable ZIP 文件名不符合发布契约：$portableName"
    }
    if ($rawName -cne $expectedRawName) {
        throw "$portableName 未对应正确的原始更新载荷。期望：$expectedRawName；实际：$rawName"
    }

    $portableArchive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPortable)
    $rawArchive = [System.IO.Compression.ZipFile]::OpenRead($resolvedRaw)
    try {
        $portableIndex = Get-ArchiveIndex -Archive $portableArchive -Label $portableName
        $rawIndex = Get-ArchiveIndex -Archive $rawArchive -Label $rawName
        if (-not $portableIndex.Files.ContainsKey($launcherEntryPath) -or
            $portableIndex.Files[$launcherEntryPath].FullName -cne $launcherEntryPath) {
            throw "$portableName 必须且只能在外层目录包含 IGoLibrary-Ex.exe。"
        }

        $launcherEntry = $portableIndex.Files[$launcherEntryPath]
        if ($launcherEntry.Length -gt $maximumLauncherBytes) {
            throw "外层启动器超过 10 MiB 门槛：$($launcherEntry.Length) bytes。"
        }
        if ($launcherEntry.CompressedLength -gt $maximumLauncherCompressedBytes) {
            throw "外层启动器 ZIP 条目超过 5 MiB 门槛：$($launcherEntry.CompressedLength) bytes。"
        }

        $launcherHash = Assert-EntryMatchesFile -Entry $launcherEntry -FilePath $resolvedLauncher
        Assert-NativeLauncherPe -Entry $launcherEntry
        $versionInfo = Get-ZipEntryVersionInfo -Entry $launcherEntry
        $expectedFileVersion = [version]::Parse("${version}.0")
        try {
            $actualFileVersion = [version]::Parse([string]$versionInfo.FileVersion)
        }
        catch {
            throw "IGoLibrary-Ex.exe 缺少有效 FileVersion：$($versionInfo.FileVersion)"
        }
        if ($actualFileVersion -ne $expectedFileVersion -or
            [string]$versionInfo.ProductVersion -cne $version) {
            throw "IGoLibrary-Ex.exe 版本资源无效。FileVersion=$($versionInfo.FileVersion)，ProductVersion=$($versionInfo.ProductVersion)，期望=$version。"
        }

        $appComparison = @{
            PortableIndex = $portableIndex
            RawIndex = $rawIndex
            Label = $portableName
        }
        Assert-AppMatchesRawPackage @appComparison

        return [pscustomobject]@{
            Path = $resolvedPortable
            Name = $portableName
            Version = $version
            Variant = $variant
            LauncherBytes = $launcherEntry.Length
            LauncherCompressedBytes = $launcherEntry.CompressedLength
            LauncherHash = $launcherHash
        }
    }
    finally {
        $rawArchive.Dispose()
        $portableArchive.Dispose()
    }
}

$primaryParameters = @{
    PortablePath = $PackagePath
    RawPath = $RawPackagePath
    LauncherPath = $PublishedLauncherPath
}
$companionParameters = @{
    PortablePath = $CompanionPackagePath
    RawPath = $CompanionRawPackagePath
    LauncherPath = $PublishedLauncherPath
}
$results = @(
    Test-PortablePackage @primaryParameters
    Test-PortablePackage @companionParameters
)

if ($results[0].Variant -ceq $results[1].Variant) {
    throw '成对验证的两个 portable ZIP 必须分别为轻量包和默认完整包。'
}
if ($results[0].Version -cne $results[1].Version) {
    throw '成对验证的两个 portable ZIP 版本不一致。'
}
if ($results[0].LauncherHash -cne $results[1].LauncherHash) {
    throw '两个 portable ZIP 中的外层启动器不一致。'
}

foreach ($result in $results) {
    $packageInfo = Get-Item -LiteralPath $result.Path
    $packageHash = (Get-FileHash -LiteralPath $result.Path -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Windows portable $($result.Variant) package verification passed: $($result.Path)"
    Write-Host "ZIP size: $($packageInfo.Length) bytes"
    Write-Host "ZIP SHA-256: $packageHash"
    Write-Host "Launcher size: $($result.LauncherBytes) bytes"
    Write-Host "Launcher compressed size: $($result.LauncherCompressedBytes) bytes"
}
