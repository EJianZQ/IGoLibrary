param(
    [Parameter(Mandatory)]
    [string]$PackagePath
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$resolvedPackage = (Resolve-Path -LiteralPath $PackagePath).Path
$packageName = [System.IO.Path]::GetFileName($resolvedPackage)
if ($packageName -notmatch '^IGoLibrary-Ex-v(?<version>(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*))-windows-x64\.zip$') {
    throw "Windows ZIP 文件名不符合发布契约：$packageName"
}
$fileNameVersion = $Matches.version

Add-Type -AssemblyName System.IO.Compression.FileSystem
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

    foreach ($requiredPath in @(
        'IGoLibrary.Ex.Desktop.exe',
        'IGoLibrary.Ex.Updater.exe',
        'portable-release.marker',
        'update-manifest.json')) {
        if (-not $entryByPath.ContainsKey($requiredPath)) {
            throw "ZIP 根目录缺少 $requiredPath。"
        }
    }

    $manifestEntry = $entryByPath['update-manifest.json']
    $manifestStream = $manifestEntry.Open()
    try {
        $memory = [System.IO.MemoryStream]::new()
        $manifestStream.CopyTo($memory)
        $manifestBytes = $memory.ToArray()
    }
    finally {
        $manifestStream.Dispose()
    }
    if ($manifestBytes.Length -ge 3 -and
        $manifestBytes[0] -eq 0xEF -and
        $manifestBytes[1] -eq 0xBB -and
        $manifestBytes[2] -eq 0xBF) {
        throw "update-manifest.json 必须使用 UTF-8 无 BOM。"
    }

    $manifestJson = [System.Text.UTF8Encoding]::new($false, $true).GetString($manifestBytes)
    $manifest = $manifestJson | ConvertFrom-Json
    if ($manifest.schemaVersion -ne 2 -or
        $manifest.product -cne 'IGoLibrary-Ex' -or
        $manifest.runtime -cne 'win-x64' -or
        $manifest.entryExecutable -cne 'IGoLibrary.Ex.Desktop.exe') {
        throw "update-manifest.json 产品契约无效。"
    }

    $portableMarkerEntry = $entryByPath['portable-release.marker']
    $portableMarkerStream = $portableMarkerEntry.Open()
    try {
        $portableMarkerMemory = [System.IO.MemoryStream]::new()
        $portableMarkerStream.CopyTo($portableMarkerMemory)
        $portableMarkerBytes = $portableMarkerMemory.ToArray()
    }
    finally {
        $portableMarkerStream.Dispose()
    }
    $expectedPortableMarkerBytes = [System.Text.UTF8Encoding]::new($false).GetBytes(
        'IGoLibrary-Ex|portable|win-x64|2')
    if ([System.Convert]::ToBase64String($portableMarkerBytes) -cne
        [System.Convert]::ToBase64String($expectedPortableMarkerBytes)) {
        throw "portable-release.marker 内容无效。"
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
        $orderedPaths += $path
        if (-not $entryByPath.ContainsKey($path)) {
            throw "manifest 声明的文件不存在：$path"
        }

        $entry = $entryByPath[$path]
        if ([long]$file.size -ne $entry.Length) {
            throw "文件大小与 manifest 不一致：$path"
        }
        $sha = [System.Security.Cryptography.SHA256]::Create()
        $stream = $entry.Open()
        try {
            $actualHash = [System.Convert]::ToHexString($sha.ComputeHash($stream)).ToLowerInvariant()
        }
        finally {
            $stream.Dispose()
            $sha.Dispose()
        }
        if ($actualHash -cne ([string]$file.sha256).ToLowerInvariant()) {
            throw "文件 SHA-256 与 manifest 不一致：$path"
        }
    }

    $expectedFileCount = $manifestPaths.Count + 1
    if ($fileEntries.Count -ne $expectedFileCount) {
        $extras = @(
            $fileEntries |
                Where-Object {
                    $_.FullName -cne 'update-manifest.json' -and
                    -not $manifestPaths.Contains($_.FullName)
                } |
                ForEach-Object FullName
        )
        throw "ZIP 文件集合与 manifest 不一致。额外文件：$($extras -join ', ')"
    }

    $sortedPaths = @($orderedPaths | Sort-Object { $_.ToUpperInvariant() }, { $_ })
    for ($index = 0; $index -lt $orderedPaths.Count; $index++) {
        if ($orderedPaths[$index] -cne $sortedPaths[$index]) {
            throw "manifest 文件列表未按不区分大小写的相对路径排序。"
        }
    }
}
finally {
    $archive.Dispose()
}

$packageInfo = Get-Item -LiteralPath $resolvedPackage
$packageHash = (Get-FileHash -LiteralPath $resolvedPackage -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host "Windows package verification passed: $resolvedPackage"
Write-Host "ZIP size: $($packageInfo.Length) bytes"
Write-Host "ZIP SHA-256: $packageHash"
