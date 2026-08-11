param(
    [Parameter(Mandatory)]
    [string]$PublishedLauncherPath,

    [Parameter(Mandatory)]
    [string]$AppVersion
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

Add-Type -AssemblyName System.IO.Compression.FileSystem

$resolvedLauncher = (Resolve-Path -LiteralPath $PublishedLauncherPath).Path
$verifyScript = Join-Path $PSScriptRoot 'verify-windows-portable-package.ps1'
$rawVerifyScript = Join-Path $PSScriptRoot 'verify-windows-package.ps1'
$testRoot = Join-Path (
    [System.IO.Path]::GetTempPath()) (
    "IGoLibrary-portable-verifier-$([Guid]::NewGuid().ToString('N'))")
$fullRawName = "IGoLibrary-Ex-v$AppVersion-windows-x64.zip"
$lightRawName = "IGoLibrary-Ex-v$AppVersion-windows-x64-without-cloudflared.zip"
$fullPortableName = "IGoLibrary-Ex-v$AppVersion-windows-x64-portable.zip"
$lightPortableName = "IGoLibrary-Ex-v$AppVersion-windows-x64-portable-without-cloudflared.zip"

function New-TestArchive {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][object[]]$Entries
    )

    $parent = [System.IO.Path]::GetDirectoryName($Path)
    if ([string]::IsNullOrWhiteSpace($parent)) {
        throw "无法确定测试 ZIP 父目录：$Path"
    }
    New-Item -ItemType Directory -Path $parent -Force | Out-Null

    $fileStream = [System.IO.FileStream]::new(
        $Path,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new(
            $fileStream,
            [System.IO.Compression.ZipArchiveMode]::Create,
            $true)
        try {
            foreach ($item in $Entries) {
                $entry = $archive.CreateEntry(
                    [string]$item.Path,
                    [System.IO.Compression.CompressionLevel]::SmallestSize)
                if ($null -ne $item.ExternalAttributes) {
                    $entry.ExternalAttributes = [int]$item.ExternalAttributes
                }

                $destination = $entry.Open()
                try {
                    if (-not [string]::IsNullOrWhiteSpace([string]$item.SourcePath)) {
                        $source = [System.IO.File]::OpenRead([string]$item.SourcePath)
                        try {
                            $source.CopyTo($destination)
                        }
                        finally {
                            $source.Dispose()
                        }
                    }
                    else {
                        $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes(
                            [string]$item.Content)
                        $destination.Write($bytes)
                    }
                }
                finally {
                    $destination.Dispose()
                }
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $fileStream.Dispose()
    }
}

function New-LauncherEntry {
    return [pscustomobject]@{
        Path = 'IGoLibrary-Ex/IGoLibrary-Ex.exe'
        SourcePath = $resolvedLauncher
        Content = $null
        ExternalAttributes = $null
    }
}

function New-ContentEntry {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Content,
        [int]$ExternalAttributes = 0
    )

    return [pscustomobject]@{
        Path = $Path
        SourcePath = $null
        Content = $Content
        ExternalAttributes = if ($ExternalAttributes -eq 0) { $null } else { $ExternalAttributes }
    }
}

function Assert-VerificationFails {
    param(
        [Parameter(Mandatory)][string]$Scenario,
        [Parameter(Mandatory)][object[]]$PortableEntries,
        [Parameter(Mandatory)][string]$RawPackage
    )

    $scenarioDirectory = Join-Path $testRoot $Scenario
    $portablePath = Join-Path $scenarioDirectory $fullPortableName
    New-TestArchive -Path $portablePath -Entries $PortableEntries
    $parameters = @{
        PackagePath = $portablePath
        RawPackagePath = $RawPackage
        CompanionPackagePath = $script:validLightPortable
        CompanionRawPackagePath = $script:validLightRaw
        PublishedLauncherPath = $resolvedLauncher
    }
    try {
        & $verifyScript @parameters
    }
    catch {
        Write-Host "Portable verifier correctly rejected $Scenario."
        return
    }

    throw "Portable verifier 未拒绝篡改场景：$Scenario"
}

function Assert-RawVerifierRejectsSeparatorOnlyEntry {
    $scenarioDirectory = Join-Path $testRoot 'raw-verifier-separator-only-entry'
    $invalidPackage = Join-Path $scenarioDirectory $fullRawName
    New-TestArchive -Path $invalidPackage -Entries @(
        New-ContentEntry -Path '////' -Content 'hidden-root-entry-data'
    )

    $verificationParameters = @{
        PackagePath = $invalidPackage
    }
    try {
        & $rawVerifyScript @verificationParameters
    }
    catch {
        if (-not $_.Exception.Message.Contains(
                'ZIP 包含非法路径',
                [System.StringComparison]::Ordinal)) {
            throw "Raw verifier 因非预期原因拒绝分隔符根条目：$($_.Exception.Message)"
        }

        Write-Host 'Raw package verifier correctly rejected separator-only-entry.'
        return
    }

    throw 'Raw package verifier 未拒绝仅由分隔符组成的 ZIP 根条目。'
}

New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
try {
    $validDirectory = Join-Path $testRoot 'valid'
    $validFullRaw = Join-Path $validDirectory $fullRawName
    $validLightRaw = Join-Path $validDirectory $lightRawName
    $validFullPortable = Join-Path $validDirectory $fullPortableName
    $validLightPortable = Join-Path $validDirectory $lightPortableName
    New-TestArchive -Path $validFullRaw -Entries @(
        New-ContentEntry -Path 'payload.txt' -Content 'full-payload'
    )
    New-TestArchive -Path $validLightRaw -Entries @(
        New-ContentEntry -Path 'payload.txt' -Content 'light-payload'
    )
    New-TestArchive -Path $validFullPortable -Entries @(
        New-LauncherEntry
        New-ContentEntry -Path 'IGoLibrary-Ex/app/payload.txt' -Content 'full-payload'
    )
    New-TestArchive -Path $validLightPortable -Entries @(
        New-LauncherEntry
        New-ContentEntry -Path 'IGoLibrary-Ex/app/payload.txt' -Content 'light-payload'
    )

    $validParameters = @{
        PackagePath = $validFullPortable
        RawPackagePath = $validFullRaw
        CompanionPackagePath = $validLightPortable
        CompanionRawPackagePath = $validLightRaw
        PublishedLauncherPath = $resolvedLauncher
    }
    & $verifyScript @validParameters
    Assert-RawVerifierRejectsSeparatorOnlyEntry

    $symbolicLinkAttributes = [System.BitConverter]::ToInt32(
        [byte[]]@(0xFF, 0x01, 0x00, 0xA0),
        0)
    $symbolicLinkEntryParameters = @{
        Path = 'IGoLibrary-Ex/app/payload.txt'
        Content = 'full-payload'
        ExternalAttributes = $symbolicLinkAttributes
    }
    $separatorOnlyRawDirectory = Join-Path $testRoot 'separator-only-raw-entry'
    $separatorOnlyRaw = Join-Path $separatorOnlyRawDirectory $fullRawName
    New-TestArchive -Path $separatorOnlyRaw -Entries @(
        New-ContentEntry -Path 'payload.txt' -Content 'full-payload'
        New-ContentEntry -Path '/' -Content 'hidden-root-entry-data'
    )
    $failureCases = @(
        [pscustomobject]@{
            Scenario = 'missing-launcher'
            Entries = @(
                New-ContentEntry -Path 'IGoLibrary-Ex/app/payload.txt' -Content 'full-payload'
            )
            RawPackage = $validFullRaw
        }
        [pscustomobject]@{
            Scenario = 'extra-root-file'
            Entries = @(
                New-LauncherEntry
                New-ContentEntry -Path 'IGoLibrary-Ex/app/payload.txt' -Content 'full-payload'
                New-ContentEntry -Path 'IGoLibrary-Ex/readme.txt' -Content 'unexpected'
            )
            RawPackage = $validFullRaw
        }
        [pscustomobject]@{
            Scenario = 'modified-app-file'
            Entries = @(
                New-LauncherEntry
                New-ContentEntry -Path 'IGoLibrary-Ex/app/payload.txt' -Content 'tampered'
            )
            RawPackage = $validFullRaw
        }
        [pscustomobject]@{
            Scenario = 'case-collision'
            Entries = @(
                New-LauncherEntry
                New-ContentEntry -Path 'IGoLibrary-Ex/app/payload.txt' -Content 'full-payload'
                New-ContentEntry -Path 'IGoLibrary-Ex/app/PAYLOAD.txt' -Content 'collision'
            )
            RawPackage = $validFullRaw
        }
        [pscustomobject]@{
            Scenario = 'path-traversal'
            Entries = @(
                New-LauncherEntry
                New-ContentEntry -Path 'IGoLibrary-Ex/app/../escape.txt' -Content 'escape'
            )
            RawPackage = $validFullRaw
        }
        [pscustomobject]@{
            Scenario = 'separator-only-forward-slash-entry'
            Entries = @(
                New-LauncherEntry
                New-ContentEntry -Path 'IGoLibrary-Ex/app/payload.txt' -Content 'full-payload'
                New-ContentEntry -Path '////' -Content 'hidden-root-entry-data'
            )
            RawPackage = $validFullRaw
        }
        [pscustomobject]@{
            Scenario = 'separator-only-backslash-entry'
            Entries = @(
                New-LauncherEntry
                New-ContentEntry -Path 'IGoLibrary-Ex/app/payload.txt' -Content 'full-payload'
                New-ContentEntry -Path '\' -Content 'hidden-root-entry-data'
            )
            RawPackage = $validFullRaw
        }
        [pscustomobject]@{
            Scenario = 'separator-only-raw-package-entry'
            Entries = @(
                New-LauncherEntry
                New-ContentEntry -Path 'IGoLibrary-Ex/app/payload.txt' -Content 'full-payload'
            )
            RawPackage = $separatorOnlyRaw
        }
        [pscustomobject]@{
            Scenario = 'symbolic-link'
            Entries = @(
                New-LauncherEntry
                New-ContentEntry @symbolicLinkEntryParameters
            )
            RawPackage = $validFullRaw
        }
        [pscustomobject]@{
            Scenario = 'variant-swap'
            Entries = @(
                New-LauncherEntry
                New-ContentEntry -Path 'IGoLibrary-Ex/app/payload.txt' -Content 'full-payload'
            )
            RawPackage = $validLightRaw
        }
    )
    foreach ($failureCase in $failureCases) {
        $failureParameters = @{
            Scenario = $failureCase.Scenario
            PortableEntries = $failureCase.Entries
            RawPackage = $failureCase.RawPackage
        }
        Assert-VerificationFails @failureParameters
    }

    Write-Host 'Portable and raw package verifier positive and tamper-rejection tests passed.'
}
finally {
    $fullTestRoot = [System.IO.Path]::GetFullPath($testRoot)
    $temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullTestRoot.StartsWith(
            $temporaryRoot,
            [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "拒绝清理临时目录之外的 verifier 测试目录：$fullTestRoot"
    }
    if ([System.IO.Directory]::Exists($fullTestRoot)) {
        [System.IO.Directory]::Delete($fullTestRoot, $true)
    }
}
