param(
    [Parameter(Mandatory)]
    [string]$PackagePath,

    [string]$CompanionPackagePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression.FileSystem

$maximumUpdaterBytes = 20MB
$maximumUpdaterCompressedBytes = 10MB

function Test-IsToolsRelativePath {
    param([Parameter(Mandatory)][string]$RelativePath)

    return $RelativePath.Equals('tools', [System.StringComparison]::OrdinalIgnoreCase) -or
        $RelativePath.StartsWith('tools/', [System.StringComparison]::OrdinalIgnoreCase)
}

function Test-IsForbiddenUpdaterSidecar {
    param([Parameter(Mandatory)][string]$RelativePath)

    $fileName = [System.IO.Path]::GetFileName($RelativePath)
    if ($fileName -ieq 'IGoLibrary.Ex.Updater.exe') {
        return $false
    }
    if ($fileName -ieq 'IGoLibrary.Ex.Updater.Core.dll') {
        return $false
    }
    if (-not $fileName.StartsWith(
            'IGoLibrary.Ex.Updater',
            [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    return $fileName.EndsWith('.pdb', [System.StringComparison]::OrdinalIgnoreCase) -or
        $fileName.EndsWith('.obj', [System.StringComparison]::OrdinalIgnoreCase) -or
        $fileName.EndsWith('.map', [System.StringComparison]::OrdinalIgnoreCase) -or
        $fileName.EndsWith('.dll', [System.StringComparison]::OrdinalIgnoreCase) -or
        $fileName.EndsWith('.deps.json', [System.StringComparison]::OrdinalIgnoreCase) -or
        $fileName.EndsWith('.runtimeconfig.json', [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-ZipEntryBytes {
    param([Parameter(Mandatory)][System.IO.Compression.ZipArchiveEntry]$Entry)

    $entryStream = $Entry.Open()
    try {
        $memory = [System.IO.MemoryStream]::new()
        try {
            $entryStream.CopyTo($memory)
            return $memory.ToArray()
        }
        finally {
            $memory.Dispose()
        }
    }
    finally {
        $entryStream.Dispose()
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
        "IGoLibrary.Ex.Updater-version-$([Guid]::NewGuid().ToString('N')).exe")
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

function Assert-EntryMatchesFile {
    param(
        [Parameter(Mandatory)][System.IO.Compression.ZipArchiveEntry]$Entry,
        [Parameter(Mandatory)][string]$SourcePath,
        [Parameter(Mandatory)][string]$Label
    )

    $entryBytes = Get-ZipEntryBytes -Entry $Entry
    $sourceBytes = [System.IO.File]::ReadAllBytes($SourcePath)
    if ([System.Convert]::ToBase64String($entryBytes) -cne
        [System.Convert]::ToBase64String($sourceBytes)) {
        throw "$Label 与仓库源文件不一致。"
    }
}

function Test-WindowsPackage {
    param([Parameter(Mandatory)][string]$Path)

    $resolvedPackage = (Resolve-Path -LiteralPath $Path).Path
    $packageName = [System.IO.Path]::GetFileName($resolvedPackage)
    $lightweightPattern = '^IGoLibrary-Ex-v(?<version>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))-windows-x64\.zip$'
    $bundledPattern = '^IGoLibrary-Ex-v(?<version>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))-windows-x64-with-cloudflared\.zip$'
    $variant = $null
    $fileNameVersion = $null
    if ($packageName -match $lightweightPattern) {
        $variant = 'Lightweight'
        $fileNameVersion = $Matches.version
    }
    elseif ($packageName -match $bundledPattern) {
        $variant = 'Bundled'
        $fileNameVersion = $Matches.version
    }
    else {
        throw "Windows ZIP 文件名不符合发布契约：$packageName"
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedPackage)
    try {
        $fileEntries = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) })
        $pathSet = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        $entryByPath = [System.Collections.Generic.Dictionary[string, System.IO.Compression.ZipArchiveEntry]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)

        foreach ($entry in $fileEntries) {
            $path = $entry.FullName
            if ($path.Contains('\') -or
                $path.StartsWith('/', [System.StringComparison]::Ordinal) -or
                $path -match '^[A-Za-z]:' -or
                $path.Split('/') -contains '..') {
                throw "ZIP 包含非法路径：$path"
            }
            if (-not $pathSet.Add($path)) {
                throw "ZIP 包含大小写重复路径：$path"
            }
            $entryByPath.Add($path, $entry)
        }

        $updaterEntries = @(
            $fileEntries | Where-Object {
                $_.Name.Equals(
                    'IGoLibrary.Ex.Updater.exe',
                    [System.StringComparison]::OrdinalIgnoreCase)
            }
        )
        if ($updaterEntries.Count -ne 1 -or
            $updaterEntries[0].FullName -cne 'IGoLibrary.Ex.Updater.exe') {
            throw 'ZIP 必须且只能在根目录包含一个名称精确为 IGoLibrary.Ex.Updater.exe 的文件。'
        }
        $updaterSidecars = @(
            $fileEntries | Where-Object {
                Test-IsForbiddenUpdaterSidecar -RelativePath $_.FullName
            }
        )
        if ($updaterSidecars.Count -gt 0) {
            throw "ZIP 不得包含 updater sidecar：$($updaterSidecars.FullName -join ', ')"
        }

        foreach ($requiredPath in @(
            'IGoLibrary.Ex.Desktop.exe',
            'IGoLibrary.Ex.Updater.exe',
            'portable-release.marker',
            'update-manifest.json')) {
            if (-not $entryByPath.ContainsKey($requiredPath)) {
                throw "ZIP 根目录缺少 $requiredPath。"
            }
        }

        $updaterEntry = $entryByPath['IGoLibrary.Ex.Updater.exe']
        if ($updaterEntry.Length -gt $maximumUpdaterBytes) {
            throw "IGoLibrary.Ex.Updater.exe 超过 20 MiB 门槛：$($updaterEntry.Length) bytes。"
        }
        if ($updaterEntry.CompressedLength -gt $maximumUpdaterCompressedBytes) {
            throw "IGoLibrary.Ex.Updater.exe 的 ZIP 条目超过 10 MiB 门槛：$($updaterEntry.CompressedLength) bytes。"
        }

        $updaterVersionInfo = Get-ZipEntryVersionInfo -Entry $updaterEntry
        try {
            $actualUpdaterFileVersion = [version]::Parse([string]$updaterVersionInfo.FileVersion)
        }
        catch {
            throw "IGoLibrary.Ex.Updater.exe 缺少有效 FileVersion：$($updaterVersionInfo.FileVersion)"
        }
        $expectedUpdaterFileVersion = [version]::Parse("${fileNameVersion}.0")
        if ($actualUpdaterFileVersion -ne $expectedUpdaterFileVersion -or
            [string]$updaterVersionInfo.ProductVersion -cne $fileNameVersion) {
            throw "IGoLibrary.Ex.Updater.exe 版本资源无效。FileVersion=$($updaterVersionInfo.FileVersion)，ProductVersion=$($updaterVersionInfo.ProductVersion)，期望=$fileNameVersion。"
        }

        $manifestBytes = Get-ZipEntryBytes -Entry $entryByPath['update-manifest.json']
        if ($manifestBytes.Length -ge 3 -and
            $manifestBytes[0] -eq 0xEF -and
            $manifestBytes[1] -eq 0xBB -and
            $manifestBytes[2] -eq 0xBF) {
            throw 'update-manifest.json 必须使用 UTF-8 无 BOM。'
        }

        $manifestJson = [System.Text.UTF8Encoding]::new($false, $true).GetString($manifestBytes)
        $manifest = $manifestJson | ConvertFrom-Json
        if ($manifest.schemaVersion -ne 2 -or
            $manifest.product -cne 'IGoLibrary-Ex' -or
            $manifest.runtime -cne 'win-x64' -or
            $manifest.entryExecutable -cne 'IGoLibrary.Ex.Desktop.exe') {
            throw 'update-manifest.json 产品契约无效。'
        }

        $portableMarkerBytes = Get-ZipEntryBytes -Entry $entryByPath['portable-release.marker']
        $expectedPortableMarkerBytes = [System.Text.UTF8Encoding]::new($false).GetBytes(
            'IGoLibrary-Ex|portable|win-x64|2')
        if ([System.Convert]::ToBase64String($portableMarkerBytes) -cne
            [System.Convert]::ToBase64String($expectedPortableMarkerBytes)) {
            throw 'portable-release.marker 内容无效。'
        }
        if ([string]$manifest.version -cne $fileNameVersion) {
            throw "ZIP 文件名版本 $fileNameVersion 与 manifest 版本 $($manifest.version) 不一致。"
        }

        $manifestPaths = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        $orderedPaths = @()
        foreach ($file in @($manifest.files)) {
            $path = [string]$file.path
            if ([string]::IsNullOrWhiteSpace($path) -or
                $path.Contains('\') -or
                $path -ceq 'update-manifest.json' -or
                -not $manifestPaths.Add($path)) {
                throw "manifest 包含非法或重复文件路径：$path"
            }
            if (Test-IsToolsRelativePath -RelativePath $path) {
                throw "manifest 不得声明 tools 保留目录：$path"
            }
            $orderedPaths += $path
            if (-not $entryByPath.ContainsKey($path)) {
                throw "manifest 声明的文件不存在：$path"
            }

            $entry = $entryByPath[$path]
            if ([long]$file.size -ne $entry.Length) {
                throw "文件大小与 manifest 不一致：$path"
            }
            $actualHash = Get-ZipEntrySha256 -Entry $entry
            if ($actualHash -cne ([string]$file.sha256).ToLowerInvariant()) {
                throw "文件 SHA-256 与 manifest 不一致：$path"
            }
        }

        if (-not $manifestPaths.Contains('IGoLibrary.Ex.Updater.exe')) {
            throw 'manifest 必须声明根目录 IGoLibrary.Ex.Updater.exe。'
        }

        $sortedPaths = @($orderedPaths | Sort-Object { $_.ToUpperInvariant() }, { $_ })
        for ($index = 0; $index -lt $orderedPaths.Count; $index++) {
            if ($orderedPaths[$index] -cne $sortedPaths[$index]) {
                throw 'manifest 文件列表未按不区分大小写的相对路径排序。'
            }
        }

        $actualExtras = @(
            $fileEntries |
                Where-Object {
                    $_.FullName -cne 'update-manifest.json' -and
                    -not $manifestPaths.Contains($_.FullName)
                } |
                ForEach-Object FullName
        )
        $expectedExtras = if ($variant -ceq 'Bundled') {
            @(
                'tools/cloudflared/cloudflared.exe',
                'tools/cloudflared/LICENSE.txt',
                'tools/cloudflared/THIRD-PARTY-NOTICES.txt'
            )
        }
        else {
            @()
        }
        $actualExtraSet = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        foreach ($extraPath in $actualExtras) {
            $null = $actualExtraSet.Add($extraPath)
        }
        $expectedExtraSet = [System.Collections.Generic.HashSet[string]]::new(
            [System.StringComparer]::OrdinalIgnoreCase)
        foreach ($extraPath in $expectedExtras) {
            $null = $expectedExtraSet.Add($extraPath)
        }
        if (-not $actualExtraSet.SetEquals($expectedExtraSet)) {
            throw "ZIP 的 manifest 外文件集合无效。实际：$($actualExtras -join ', ')"
        }

        if ($variant -ceq 'Lightweight') {
            $toolsEntry = $fileEntries | Where-Object {
                Test-IsToolsRelativePath -RelativePath $_.FullName
            } | Select-Object -First 1
            if ($null -ne $toolsEntry) {
                throw "轻量包不得包含 tools：$($toolsEntry.FullName)"
            }
        }
        else {
            $assetManifest = Get-Content -Raw -LiteralPath (Join-Path $PSScriptRoot 'cloudflared-assets.json') |
                ConvertFrom-Json
            $cloudflaredHash = Get-ZipEntrySha256 -Entry $entryByPath['tools/cloudflared/cloudflared.exe']
            $expectedCloudflaredHash = ([string]$assetManifest.assets.'win-x64'.sha256).ToLowerInvariant()
            if ($cloudflaredHash -cne $expectedCloudflaredHash) {
                throw "cloudflared.exe SHA-256 无效：$cloudflaredHash"
            }
            $licenseComparison = @{
                Entry = $entryByPath['tools/cloudflared/LICENSE.txt']
                SourcePath = Join-Path $PSScriptRoot 'third-party\cloudflared-LICENSE.txt'
                Label = 'cloudflared LICENSE.txt'
            }
            Assert-EntryMatchesFile @licenseComparison
            $noticeComparison = @{
                Entry = $entryByPath['tools/cloudflared/THIRD-PARTY-NOTICES.txt']
                SourcePath = Join-Path $PSScriptRoot 'third-party\THIRD-PARTY-NOTICES.txt'
                Label = 'cloudflared THIRD-PARTY-NOTICES.txt'
            }
            Assert-EntryMatchesFile @noticeComparison
        }

        return [pscustomobject]@{
            Path = $resolvedPackage
            Name = $packageName
            Version = $fileNameVersion
            Variant = $variant
            ManifestBytes = $manifestBytes
            UpdaterBytes = $updaterEntry.Length
            UpdaterCompressedBytes = $updaterEntry.CompressedLength
            UpdaterFileVersion = $updaterVersionInfo.FileVersion
            UpdaterProductVersion = $updaterVersionInfo.ProductVersion
        }
    }
    finally {
        $archive.Dispose()
    }
}

$results = @(Test-WindowsPackage -Path $PackagePath)
if (-not [string]::IsNullOrWhiteSpace($CompanionPackagePath)) {
    $results += Test-WindowsPackage -Path $CompanionPackagePath
    if ($results[0].Variant -ceq $results[1].Variant) {
        throw '成对验证的两个 Windows ZIP 必须分别为轻量包和 cloudflared 完整包。'
    }
    if ($results[0].Version -cne $results[1].Version) {
        throw '成对验证的两个 Windows ZIP 版本不一致。'
    }
    if ([System.Convert]::ToBase64String($results[0].ManifestBytes) -cne
        [System.Convert]::ToBase64String($results[1].ManifestBytes)) {
        throw '轻量包与 cloudflared 完整包的 update-manifest.json 不一致。'
    }
}

foreach ($result in $results) {
    $packageInfo = Get-Item -LiteralPath $result.Path
    $packageHash = (Get-FileHash -LiteralPath $result.Path -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "Windows $($result.Variant) package verification passed: $($result.Path)"
    Write-Host "ZIP size: $($packageInfo.Length) bytes"
    Write-Host "ZIP SHA-256: $packageHash"
    Write-Host "Updater size: $($result.UpdaterBytes) bytes"
    Write-Host "Updater compressed size: $($result.UpdaterCompressedBytes) bytes"
    Write-Host "Updater FileVersion/ProductVersion: $($result.UpdaterFileVersion) / $($result.UpdaterProductVersion)"
}
