param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [switch]$SelfContained = $true,
    [string]$AppVersion,
    [string]$PackageName,
    [string]$BundledPackageName,
    [string]$ManagedUpdaterBaselinePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$maximumUpdaterBytes = 20MB
$maximumUpdaterCompressedBytes = 10MB
$maximumLauncherBytes = 10MB
$maximumLauncherCompressedBytes = 5MB

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
$launcherProject = Join-Path $root 'src\IGoLibrary.Ex.Launcher\IGoLibrary.Ex.Launcher.csproj'
$launcherTestsProject = Join-Path $root 'tests\IGoLibrary.Ex.Launcher.Tests\IGoLibrary.Ex.Launcher.Tests.csproj'
$updaterAcceptanceProject = Join-Path $root 'tests\IGoLibrary.Ex.Updater.AcceptanceTests\IGoLibrary.Ex.Updater.AcceptanceTests.csproj'
if ([string]::IsNullOrWhiteSpace($ManagedUpdaterBaselinePath)) {
    $ManagedUpdaterBaselinePath = Join-Path $root 'artifacts\validation\managed-updater-baseline\IGoLibrary.Ex.Updater.exe'
}
$ManagedUpdaterBaselinePath = [System.IO.Path]::GetFullPath($ManagedUpdaterBaselinePath, $root)
if (-not (Test-Path -LiteralPath $ManagedUpdaterBaselinePath -PathType Leaf)) {
    throw "缺少 managed updater 迁移基线：$ManagedUpdaterBaselinePath。请从上一稳定版保留 IGoLibrary.Ex.Updater.exe，或通过 -ManagedUpdaterBaselinePath 显式指定。"
}
$output = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "publish\$Runtime"))
$updaterOutput = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "publish\updater-$Runtime"))
$launcherOutput = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "publish\launcher-$Runtime"))
$symbolsOutput = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "symbols\$Runtime\v$AppVersion"))
$packageOutput = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "windows\$Runtime"))
$lightweightStaging = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "staging\windows\$Runtime\no-tools"))
$portableStaging = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "staging\windows\$Runtime\portable"))
$packageStaging = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "staging\windows\$Runtime\packages"))
$expectedPackageName = "IGoLibrary-Ex-v$AppVersion-windows-x64-without-cloudflared.zip"
$expectedBundledPackageName = "IGoLibrary-Ex-v$AppVersion-windows-x64.zip"
$portablePackageName = "IGoLibrary-Ex-v$AppVersion-windows-x64-portable-without-cloudflared.zip"
$portableBundledPackageName = "IGoLibrary-Ex-v$AppVersion-windows-x64-portable.zip"
$legacyBundledPackageName = "IGoLibrary-Ex-v$AppVersion-windows-x64-with-cloudflared.zip"
if ([string]::IsNullOrWhiteSpace($PackageName)) {
    $PackageName = $expectedPackageName
}
if ([string]::IsNullOrWhiteSpace($BundledPackageName)) {
    $BundledPackageName = $expectedBundledPackageName
}
if ($PackageName -cne $expectedPackageName) {
    throw "Windows 不含 cloudflared 的轻量包名称必须为 $expectedPackageName。"
}
if ($BundledPackageName -cne $expectedBundledPackageName) {
    throw "Windows 默认 cloudflared 完整包名称必须为 $expectedBundledPackageName。"
}
if ($PackageName -ieq $BundledPackageName) {
    throw '轻量包和 cloudflared 完整包不能使用同一个文件名。'
}

$zipPath = Join-Path $packageOutput $PackageName
$bundledZipPath = Join-Path $packageOutput $BundledPackageName
$portableZipPath = Join-Path $packageOutput $portablePackageName
$portableBundledZipPath = Join-Path $packageOutput $portableBundledPackageName
$legacyBundledZipPath = Join-Path $packageOutput $legacyBundledPackageName
$stagedZipPath = Join-Path $packageStaging $PackageName
$stagedBundledZipPath = Join-Path $packageStaging $BundledPackageName
$stagedPortableZipPath = Join-Path $packageStaging $portablePackageName
$stagedPortableBundledZipPath = Join-Path $packageStaging $portableBundledPackageName
$portableLightweightStaging = Join-Path $portableStaging 'lightweight'
$portableBundledStaging = Join-Path $portableStaging 'default'
$stagedSymbolsOutput = Join-Path $packageStaging 'symbols'

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

function Assert-NativeAotToolchain {
    if (-not [System.OperatingSystem]::IsWindows()) {
        throw 'Windows Native AOT 发布必须在 Windows x64 构建机上运行。'
    }

    $vsWherePath = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (-not (Test-Path -LiteralPath $vsWherePath -PathType Leaf)) {
        throw '找不到 Visual Studio Installer/vswhere。请安装 Visual Studio Build Tools。'
    }

    $vsWhereArguments = @(
        '-latest'
        '-prerelease'
        '-products'
        '*'
        '-requires'
        'Microsoft.VisualStudio.Component.VC.Tools.x86.x64'
        '-property'
        'installationPath'
    )
    $installationPaths = @(& $vsWherePath @vsWhereArguments)
    $vsWhereExitCode = $LASTEXITCODE
    if ($vsWhereExitCode -ne 0) {
        throw "vswhere 检查 Native AOT 工具链失败，退出码：$vsWhereExitCode。"
    }

    $installationPath = $installationPaths |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($installationPath)) {
        throw '缺少 Windows Native AOT 工具链。请在 Visual Studio Installer 中安装“使用 C++ 的桌面开发”工作负载及 Windows 10/11 SDK。'
    }

    $vcVarsAllPath = Join-Path $installationPath 'VC\Auxiliary\Build\vcvarsall.bat'
    if (-not (Test-Path -LiteralPath $vcVarsAllPath -PathType Leaf)) {
        throw "Visual Studio C++ 工具链不完整，缺少：$vcVarsAllPath"
    }

    $msvcToolsRoot = Join-Path $installationPath 'VC\Tools\MSVC'
    if (-not (Test-Path -LiteralPath $msvcToolsRoot -PathType Container)) {
        throw "Visual Studio C++ 工具链不完整，缺少 MSVC 工具目录：$msvcToolsRoot"
    }
    $msvcToolsetDirectory = Get-ChildItem -LiteralPath $msvcToolsRoot -Directory |
        Sort-Object Name -Descending |
        Where-Object {
            (Test-Path -LiteralPath (Join-Path $_.FullName 'bin\Hostx64\x64\link.exe') -PathType Leaf) -and
            (Test-Path -LiteralPath (Join-Path $_.FullName 'lib\x64\libcmt.lib') -PathType Leaf)
        } |
        Select-Object -First 1
    if ($null -eq $msvcToolsetDirectory) {
        throw 'Visual Studio C++ 工具链不完整：找不到 x64 link.exe 或 libcmt.lib。请修复“使用 C++ 的桌面开发”工作负载。'
    }

    $windowsKitsRoot = [Microsoft.Win32.Registry]::GetValue(
        'HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows Kits\Installed Roots',
        'KitsRoot10',
        $null)
    if ([string]::IsNullOrWhiteSpace($windowsKitsRoot)) {
        $windowsKitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
    }
    $windowsSdkLibRoot = Join-Path $windowsKitsRoot 'Lib'
    $windowsSdkDirectory = if (Test-Path -LiteralPath $windowsSdkLibRoot -PathType Container) {
        Get-ChildItem -LiteralPath $windowsSdkLibRoot -Directory |
            Sort-Object Name -Descending |
            Where-Object {
                (Test-Path -LiteralPath (Join-Path $_.FullName 'um\x64\kernel32.lib') -PathType Leaf) -and
                (Test-Path -LiteralPath (Join-Path $_.FullName 'ucrt\x64\ucrt.lib') -PathType Leaf)
            } |
            Select-Object -First 1
    }
    if ($null -eq $windowsSdkDirectory) {
        throw 'Windows SDK 不完整：找不到 x64 kernel32.lib 或 ucrt.lib。请在 Visual Studio Installer 中安装 Windows 10/11 SDK。'
    }

    Write-Host "Native AOT Visual Studio: $installationPath"
    Write-Host "Native AOT MSVC toolset: $($msvcToolsetDirectory.Name)"
    Write-Host "Native AOT Windows SDK: $($windowsSdkDirectory.Name)"
}

function Assert-UpdaterHeadlessSmoke {
    param([Parameter(Mandatory)][string]$ExecutablePath)

    $missingRequestPath = Join-Path (
        [System.IO.Path]::GetDirectoryName($ExecutablePath)) (
        ".aot-smoke-missing-$([Guid]::NewGuid().ToString('N')).json")
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ExecutablePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in @('--worker', '--request', $missingRequestPath)) {
        $null = $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw '无法启动 Native AOT updater 冒烟测试进程。'
    }
    try {
        if (-not $process.WaitForExit(10000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw 'Native AOT updater 无界面冒烟测试超时；进程可能错误进入了 UI 或发生死锁。'
        }
        if ($process.ExitCode -ne 1) {
            throw "Native AOT updater 无界面冒烟测试退出码无效：$($process.ExitCode)，期望：1。"
        }
    }
    finally {
        $process.Dispose()
    }

    Write-Host 'Native AOT updater headless smoke test passed.'
}

function Assert-UpdaterTaskDialogSmoke {
    param([Parameter(Mandatory)][string]$ExecutablePath)

    $smokeDirectory = Join-Path (
        [System.IO.Path]::GetTempPath()) (
        "IGoLibrary-Aot-TaskDialog-$([Guid]::NewGuid().ToString('N'))")
    New-Item -ItemType Directory -Path $smokeDirectory | Out-Null
    $requestPath = Join-Path $smokeDirectory 'request.json'
    $requestStream = [System.IO.FileStream]::new(
        $requestPath,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::Write,
        [System.IO.FileShare]::None)
    try {
        $requestStream.Write([byte[]]@(0x7B, 0x7D))
        $padding = [byte[]]::new(1MB)
        [System.Array]::Fill[byte]($padding, [byte]0x20)
        for ($index = 0; $index -lt 16; $index++) {
            $requestStream.Write($padding)
        }
    }
    finally {
        $requestStream.Dispose()
    }

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $ExecutablePath
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in @('--request', $requestPath)) {
        $null = $startInfo.ArgumentList.Add($argument)
    }

    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        [System.IO.Directory]::Delete($smokeDirectory, $true)
        throw '无法启动 Native AOT updater TaskDialog 冒烟测试进程。'
    }

    try {
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        $progressDialogObserved = $false
        while ([DateTime]::UtcNow -lt $deadline -and -not $process.HasExited) {
            $process.Refresh()
            $title = $process.MainWindowTitle
            if ($title -ceq '我去图书馆 - 更新程序') {
                throw 'Native AOT updater 未能创建 TaskDialog，已退回 MessageBox；拒绝发布。'
            }
            if ($title -ceq '我去图书馆 - 正在更新') {
                $progressDialogObserved = $true
            }

            Start-Sleep -Milliseconds 5
        }

        if (-not $progressDialogObserved) {
            $exitDescription = if ($process.HasExited) {
                "进程已退出，退出码：$($process.ExitCode)"
            }
            else {
                '进程仍在运行'
            }
            throw "Native AOT updater TaskDialog 冒烟测试未观察到进度窗口；$exitDescription。"
        }

        if (-not $process.HasExited -and -not $process.WaitForExit(10000)) {
            throw 'Native AOT updater TaskDialog 冒烟测试在请求校验失败后未退出。'
        }
        if ($process.ExitCode -ne 1) {
            throw "Native AOT updater TaskDialog 冒烟测试退出码无效：$($process.ExitCode)，期望：1。"
        }
    }
    finally {
        if (-not $process.HasExited) {
            $process.Kill($true)
            $process.WaitForExit()
        }
        $process.Dispose()
        if (Test-Path -LiteralPath $smokeDirectory -PathType Container) {
            [System.IO.Directory]::Delete($smokeDirectory, $true)
        }
    }

    $errorStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $errorStartInfo.FileName = $ExecutablePath
    $errorStartInfo.UseShellExecute = $false
    $errorStartInfo.CreateNoWindow = $true
    $errorProcess = [System.Diagnostics.Process]::Start($errorStartInfo)
    if ($null -eq $errorProcess) {
        throw '无法启动 Native AOT updater 参数错误对话框冒烟测试进程。'
    }
    try {
        $errorDeadline = [DateTime]::UtcNow.AddSeconds(10)
        $errorDialogObserved = $false
        while ([DateTime]::UtcNow -lt $errorDeadline -and -not $errorProcess.HasExited) {
            $errorProcess.Refresh()
            if ($errorProcess.MainWindowTitle -ceq '我去图书馆 - 更新程序') {
                $errorDialogObserved = $true
                break
            }

            Start-Sleep -Milliseconds 20
        }
        if (-not $errorDialogObserved) {
            throw 'Native AOT updater 未显示预期的参数错误对话框。'
        }
        if (-not $errorProcess.CloseMainWindow()) {
            throw 'Native AOT updater 参数错误对话框不接受系统关闭。'
        }
        if (-not $errorProcess.WaitForExit(5000)) {
            throw 'Native AOT updater 参数错误对话框未响应系统关闭、Esc/Alt+F4 等价路径。'
        }
        if ($errorProcess.ExitCode -ne 2) {
            throw "Native AOT updater 参数错误退出码无效：$($errorProcess.ExitCode)，期望：2。"
        }
    }
    finally {
        if (-not $errorProcess.HasExited) {
            $errorProcess.Kill($true)
            $errorProcess.WaitForExit()
        }
        $errorProcess.Dispose()
    }

    Write-Host 'Native AOT updater TaskDialog smoke test passed.'
}

function Assert-LauncherUnitTests {
    param(
        [Parameter(Mandatory)][string]$TestProjectPath,
        [Parameter(Mandatory)][string]$BuildConfiguration
    )

    $testArguments = @(
        'test'
        $TestProjectPath
        '--configuration'
        $BuildConfiguration
        '--nologo'
        '--verbosity'
        'minimal'
        '-m:1'
        '-p:UseSharedCompilation=false'
    )
    & dotnet @testArguments
    $testExitCode = $LASTEXITCODE
    if ($testExitCode -ne 0) {
        throw "Launcher unit tests failed with exit code $testExitCode."
    }

    Write-Host 'Windows launcher unit tests passed.'
}

function Assert-PublishedLauncherBinary {
    param(
        [Parameter(Mandatory)][string]$ExecutablePath,
        [Parameter(Mandatory)][string]$OutputDirectory
    )

    if (-not (Test-Path -LiteralPath $ExecutablePath -PathType Leaf)) {
        throw "Published launcher was not found: $ExecutablePath"
    }

    $launcherInfo = Get-Item -LiteralPath $ExecutablePath
    if ($launcherInfo.Length -gt $maximumLauncherBytes) {
        throw "Native AOT launcher 超过 10 MiB 门槛：$($launcherInfo.Length) bytes。"
    }

    $allowedFiles = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::Ordinal)
    $null = $allowedFiles.Add('IGoLibrary-Ex.exe')
    $null = $allowedFiles.Add('IGoLibrary-Ex.pdb')
    $unexpectedFiles = @(
        Get-ChildItem -LiteralPath $OutputDirectory -File -Recurse | Where-Object {
            $relativePath = [System.IO.Path]::GetRelativePath(
                $OutputDirectory,
                $_.FullName).Replace('\', '/')
            -not $allowedFiles.Contains($relativePath)
        }
    )
    if ($unexpectedFiles.Count -gt 0) {
        throw "Native AOT launcher 发布输出包含额外 sidecar：$($unexpectedFiles.Name -join ', ')"
    }

    $launcherPdb = Join-Path $OutputDirectory 'IGoLibrary-Ex.pdb'
    if (-not (Test-Path -LiteralPath $launcherPdb -PathType Leaf)) {
        throw "Native AOT launcher 缺少内部故障诊断所需的 PDB：$launcherPdb"
    }

    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($ExecutablePath)
    $expectedFileVersion = [version]::Parse("${AppVersion}.0")
    $actualFileVersion = [version]::Parse($versionInfo.FileVersion)
    if ($actualFileVersion -ne $expectedFileVersion -or
        $versionInfo.ProductVersion -cne $AppVersion -or
        $versionInfo.ProductName -cne '我去图书馆' -or
        $versionInfo.FileDescription -cne '我去图书馆启动器') {
        throw "Launcher 版本资源不匹配。FileDescription=$($versionInfo.FileDescription)，ProductName=$($versionInfo.ProductName)，FileVersion=$($versionInfo.FileVersion)，ProductVersion=$($versionInfo.ProductVersion)。"
    }

    $stream = [System.IO.File]::OpenRead($ExecutablePath)
    try {
        $reader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        try {
            if ([string]$reader.PEHeaders.CoffHeader.Machine -cne 'Amd64') {
                throw 'Native AOT launcher 必须是 AMD64 PE。'
            }
            if ([string]$reader.PEHeaders.PEHeader.Subsystem -cne 'WindowsGui') {
                throw 'Native AOT launcher 必须使用 Windows GUI 子系统。'
            }
            if ($null -ne $reader.PEHeaders.CorHeader) {
                throw 'Native AOT launcher 不得包含 CLR 入口。'
            }
        }
        finally {
            $reader.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    Write-Host 'Native AOT launcher binary verification passed.'
}

function Wait-ForLauncherSmokeFile {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][System.Diagnostics.Process]$LauncherProcess,
        [Parameter(Mandatory)][string]$Label
    )

    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    while ([DateTime]::UtcNow -lt $deadline) {
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            return
        }
        if (-not $LauncherProcess.HasExited) {
            Start-Sleep -Milliseconds 20
            continue
        }

        Start-Sleep -Milliseconds 20
    }

    throw "Native AOT launcher 冒烟测试未生成 $Label：$Path"
}

function Remove-LauncherSmokeDirectory {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    $temporaryRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $fullPath.StartsWith($temporaryRoot, $pathComparison)) {
        throw "拒绝清理临时目录之外的 launcher 冒烟目录：$fullPath"
    }

    for ($attempt = 1; $attempt -le 30; $attempt++) {
        try {
            if ([System.IO.Directory]::Exists($fullPath)) {
                [System.IO.Directory]::Delete($fullPath, $true)
            }
            return
        }
        catch [System.IO.IOException], [System.UnauthorizedAccessException] {
            if ($attempt -eq 30) {
                throw
            }
            Start-Sleep -Milliseconds 100
        }
    }
}

function Assert-PublishedLauncherSmoke {
    param(
        [Parameter(Mandatory)][string]$ExecutablePath,
        [Parameter(Mandatory)][string]$BuildConfiguration
    )

    $testProcessOutput = Join-Path $root "tests\IGoLibrary.Ex.TestProcess\bin\$BuildConfiguration\net10.0"
    $testProcessExecutable = Join-Path $testProcessOutput 'IGoLibrary.Ex.TestProcess.exe'
    $requiredHelperFiles = @(
        $testProcessExecutable,
        (Join-Path $testProcessOutput 'IGoLibrary.Ex.TestProcess.dll'),
        (Join-Path $testProcessOutput 'IGoLibrary.Ex.TestProcess.deps.json'),
        (Join-Path $testProcessOutput 'IGoLibrary.Ex.TestProcess.runtimeconfig.json')
    )
    foreach ($requiredFile in $requiredHelperFiles) {
        if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
            throw "Launcher 冒烟测试缺少 TestProcess 构建产物：$requiredFile"
        }
    }

    $smokeRoot = Join-Path (
        [System.IO.Path]::GetTempPath()) (
        "IGoLibrary-Launcher-含 空格与中文-$([Guid]::NewGuid().ToString('N'))")
    $launcherDirectory = Join-Path $smokeRoot 'IGoLibrary-Ex'
    $appDirectory = Join-Path $launcherDirectory 'app'
    $smokeLauncher = Join-Path $launcherDirectory 'IGoLibrary-Ex.exe'
    $fakeEntryExecutable = Join-Path $appDirectory 'IGoLibrary.Ex.Desktop.exe'
    $recordPath = Join-Path $smokeRoot 'launch-record.json'
    $readyPath = Join-Path $smokeRoot 'child-ready.txt'
    $releasePath = Join-Path $smokeRoot 'child-release.txt'
    $launcherProcess = $null
    $childProcess = $null

    try {
        New-Item -ItemType Directory -Path $appDirectory -Force | Out-Null
        Copy-Item -LiteralPath $ExecutablePath -Destination $smokeLauncher
        Copy-Item -LiteralPath $testProcessExecutable -Destination $fakeEntryExecutable
        foreach ($helperFile in $requiredHelperFiles | Select-Object -Skip 1) {
            Copy-Item -LiteralPath $helperFile -Destination $appDirectory
        }

        $forwardedArguments = @(
            '中文参数'
            'value with spaces'
            ''
            '"quoted"'
            'C:\尾部\'
        )
        $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $startInfo.FileName = $smokeLauncher
        $startInfo.WorkingDirectory = [System.IO.Path]::GetTempPath()
        $startInfo.UseShellExecute = $false
        foreach ($argument in @(
            'record-launch',
            $recordPath,
            $readyPath,
            $releasePath) + $forwardedArguments) {
            $null = $startInfo.ArgumentList.Add($argument)
        }

        $launcherProcess = [System.Diagnostics.Process]::Start($startInfo)
        if ($null -eq $launcherProcess) {
            throw '无法启动 Native AOT launcher 冒烟测试进程。'
        }
        if (-not $launcherProcess.WaitForExit(10000)) {
            throw 'Native AOT launcher 未在 10 秒内退出，可能错误等待了 Desktop。'
        }
        if ($launcherProcess.ExitCode -ne 0) {
            throw "Native AOT launcher 冒烟测试退出码无效：$($launcherProcess.ExitCode)，期望：0。"
        }

        Wait-ForLauncherSmokeFile -Path $readyPath -LauncherProcess $launcherProcess -Label '子进程就绪信号'
        Wait-ForLauncherSmokeFile -Path $recordPath -LauncherProcess $launcherProcess -Label '启动记录'
        $record = Get-Content -Raw -LiteralPath $recordPath | ConvertFrom-Json
        $childProcess = [System.Diagnostics.Process]::GetProcessById([int]$record.processId)
        if ($childProcess.HasExited) {
            throw 'Native AOT launcher 退出后 TestProcess 未保持运行。'
        }
        if (-not [string]::Equals(
                [System.IO.Path]::GetFullPath([string]$record.processPath),
                [System.IO.Path]::GetFullPath($fakeEntryExecutable),
                $pathComparison)) {
            throw "Launcher 启动了错误的内层 EXE：$($record.processPath)"
        }
        if (-not [string]::Equals(
                [System.IO.Path]::GetFullPath([string]$record.currentDirectory),
                [System.IO.Path]::GetFullPath($appDirectory),
                $pathComparison)) {
            throw "Launcher 设置了错误的工作目录：$($record.currentDirectory)"
        }

        $actualArguments = @($record.arguments)
        if ($actualArguments.Count -ne $forwardedArguments.Count) {
            throw 'Launcher 未完整保留参数数量。'
        }
        for ($index = 0; $index -lt $forwardedArguments.Count; $index++) {
            if ([string]$actualArguments[$index] -cne $forwardedArguments[$index]) {
                throw "Launcher 参数边界发生变化，索引：$index。"
            }
        }

        [System.IO.File]::WriteAllText($releasePath, 'release')
        if (-not $childProcess.WaitForExit(10000)) {
            throw 'Launcher 冒烟测试的内层 TestProcess 未按 release marker 退出。'
        }
        $childProcess.Dispose()
        $childProcess = $null
        $launcherProcess.Dispose()
        $launcherProcess = $null

        Remove-Item -LiteralPath $fakeEntryExecutable -Force
        $errorStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $errorStartInfo.FileName = $smokeLauncher
        $errorStartInfo.WorkingDirectory = [System.IO.Path]::GetTempPath()
        $errorStartInfo.UseShellExecute = $false
        $launcherProcess = [System.Diagnostics.Process]::Start($errorStartInfo)
        if ($null -eq $launcherProcess) {
            throw '无法启动 Native AOT launcher 错误对话框冒烟测试。'
        }

        $dialogDeadline = [DateTime]::UtcNow.AddSeconds(10)
        $dialogObserved = $false
        while ([DateTime]::UtcNow -lt $dialogDeadline -and -not $launcherProcess.HasExited) {
            $launcherProcess.Refresh()
            if ($launcherProcess.MainWindowTitle -ceq '我去图书馆 - 启动失败') {
                $dialogObserved = $true
                break
            }
            Start-Sleep -Milliseconds 20
        }
        if (-not $dialogObserved) {
            throw 'Native AOT launcher 未显示预期的启动失败对话框。'
        }
        if (-not $launcherProcess.CloseMainWindow()) {
            throw 'Native AOT launcher 启动失败对话框不接受系统关闭。'
        }
        if (-not $launcherProcess.WaitForExit(5000)) {
            throw 'Native AOT launcher 启动失败对话框未在关闭后退出。'
        }
        if ($launcherProcess.ExitCode -ne 2) {
            throw "Native AOT launcher 部署错误退出码无效：$($launcherProcess.ExitCode)，期望：2。"
        }
    }
    finally {
        if ([System.IO.Directory]::Exists($smokeRoot) -and
            -not (Test-Path -LiteralPath $releasePath -PathType Leaf)) {
            [System.IO.File]::WriteAllText($releasePath, 'release')
        }
        if ($null -ne $childProcess) {
            if (-not $childProcess.HasExited) {
                if (-not $childProcess.WaitForExit(3000)) {
                    $childProcess.Kill($true)
                    $childProcess.WaitForExit()
                }
            }
            $childProcess.Dispose()
        }
        if ($null -ne $launcherProcess) {
            if (-not $launcherProcess.HasExited) {
                $launcherProcess.Kill($true)
                $launcherProcess.WaitForExit()
            }
            $launcherProcess.Dispose()
        }
        Remove-LauncherSmokeDirectory -Path $smokeRoot
    }

    Write-Host 'Native AOT launcher published smoke tests passed.'
}

function Assert-PublishedUpdaterTransactions {
    param(
        [Parameter(Mandatory)][string]$AotUpdaterPath,
        [Parameter(Mandatory)][string]$ManagedBaselinePath,
        [Parameter(Mandatory)][string]$TestProjectPath,
        [Parameter(Mandatory)][string]$BuildConfiguration
    )

    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop |
        Select-Object -First 1
    $testProcessOutput = Join-Path $root "tests\IGoLibrary.Ex.TestProcess\bin\$BuildConfiguration\net10.0"
    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $dotnetCommand.Source
    $startInfo.UseShellExecute = $false
    $startInfo.WorkingDirectory = $root
    foreach ($argument in @(
        'test'
        $TestProjectPath
        '--configuration'
        $BuildConfiguration
        '--nologo'
        '--verbosity'
        'minimal'
        '-m:1'
        '-p:UseSharedCompilation=false'
    )) {
        $null = $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.Environment['IGOLIBRARY_AOT_UPDATER_PATH'] = $AotUpdaterPath
    $startInfo.Environment['IGOLIBRARY_MANAGED_UPDATER_BASELINE_PATH'] = $ManagedBaselinePath
    $startInfo.Environment['IGOLIBRARY_TEST_PROCESS_OUTPUT'] = $testProcessOutput

    Write-Host 'Running published Native AOT transaction acceptance matrix...'
    Write-Host "Managed migration baseline SHA-256: $((Get-FileHash -LiteralPath $ManagedBaselinePath -Algorithm SHA256).Hash)"
    $process = [System.Diagnostics.Process]::Start($startInfo)
    if ($null -eq $process) {
        throw '无法启动发布后 updater 事务验收。'
    }
    try {
        if (-not $process.WaitForExit(15 * 60 * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw '发布后 updater 事务验收超过 15 分钟，已终止。'
        }
        if ($process.ExitCode -ne 0) {
            throw "发布后 updater 事务验收失败，dotnet test 退出码：$($process.ExitCode)。"
        }
    }
    finally {
        $process.Dispose()
    }

    Write-Host 'Published Native AOT transaction acceptance matrix passed.'
}

function Test-IsToolsRelativePath {
    param([Parameter(Mandatory)][string]$RelativePath)

    return $RelativePath.Equals('tools', [System.StringComparison]::OrdinalIgnoreCase) -or
        $RelativePath.StartsWith('tools/', [System.StringComparison]::OrdinalIgnoreCase)
}

$managedCloudflaredRelativePaths = @(
    'tools/cloudflared/cloudflared.exe',
    'tools/cloudflared/LICENSE.txt',
    'tools/cloudflared/THIRD-PARTY-NOTICES.txt'
)

function Test-IsManagedCloudflaredRelativePath {
    param([Parameter(Mandatory)][string]$RelativePath)

    foreach ($managedPath in $managedCloudflaredRelativePaths) {
        if ($RelativePath.Equals($managedPath, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

function Test-IsManagedCloudflaredContainerRelativePath {
    param([Parameter(Mandatory)][string]$RelativePath)

    return $RelativePath.Equals('tools', [System.StringComparison]::OrdinalIgnoreCase) -or
        $RelativePath.Equals('tools/cloudflared', [System.StringComparison]::OrdinalIgnoreCase)
}

function Write-UpdateManifest {
    param(
        [Parameter(Mandatory)][string]$RootDirectory,
        [Parameter(Mandatory)][bool]$IncludeCloudflared
    )

    $toolDirectories = @(
        Get-ChildItem -LiteralPath $RootDirectory -Directory -Recurse | ForEach-Object {
            [System.IO.Path]::GetRelativePath($RootDirectory, $_.FullName).Replace('\', '/')
        } | Where-Object { Test-IsToolsRelativePath -RelativePath $_ }
    )
    foreach ($relativePath in $toolDirectories) {
        if (-not $IncludeCloudflared) {
            throw "不含 cloudflared 的轻量包不得包含 tools 目录：$relativePath"
        }
        if (-not (Test-IsManagedCloudflaredContainerRelativePath -RelativePath $relativePath)) {
            throw "默认完整包包含不受支持的 tools 目录：$relativePath"
        }
    }

    $manifestFiles = @(
        Get-ChildItem -LiteralPath $RootDirectory -File -Recurse | ForEach-Object {
            $relativePath = [System.IO.Path]::GetRelativePath($RootDirectory, $_.FullName).Replace('\', '/')
            if ($relativePath -ceq 'update-manifest.json') {
                return
            }

            if (Test-IsToolsRelativePath -RelativePath $relativePath) {
                if (-not $IncludeCloudflared) {
                    throw "不含 cloudflared 的轻量包不得包含 tools 文件：$relativePath"
                }
                if (-not (Test-IsManagedCloudflaredRelativePath -RelativePath $relativePath)) {
                    throw "默认完整包包含不受支持的 tools 文件：$relativePath"
                }
            }

            [pscustomobject][ordered]@{
                path = $relativePath
                size = [long]$_.Length
                sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            }
        } | Sort-Object { $_.path.ToUpperInvariant() }, path
    )

    $actualManagedPaths = @(
        $manifestFiles |
            Where-Object { Test-IsManagedCloudflaredRelativePath -RelativePath $_.path } |
            ForEach-Object path
    )
    $actualManagedSet = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $actualManagedPaths) {
        $null = $actualManagedSet.Add($path)
    }
    $expectedManagedSet = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    if ($IncludeCloudflared) {
        foreach ($path in $managedCloudflaredRelativePaths) {
            $null = $expectedManagedSet.Add($path)
        }
    }
    if (-not $actualManagedSet.SetEquals($expectedManagedSet)) {
        $variant = if ($IncludeCloudflared) { '默认完整包' } else { '轻量包' }
        throw "$variant 的 cloudflared manifest 文件集合无效。实际：$($actualManagedPaths -join ', ')"
    }

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
        (Join-Path $RootDirectory 'update-manifest.json'),
        $manifestJson,
        [System.Text.UTF8Encoding]::new($false))
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

function New-PortablePackageTree {
    param(
        [Parameter(Mandatory)][string]$AppSource,
        [Parameter(Mandatory)][string]$Destination,
        [Parameter(Mandatory)][string]$LauncherPath
    )

    $portableRootDirectory = Join-Path $Destination 'IGoLibrary-Ex'
    $appDestination = Join-Path $portableRootDirectory 'app'
    New-Item -ItemType Directory -Path $appDestination -Force | Out-Null
    Copy-Item -LiteralPath $LauncherPath -Destination (
        Join-Path $portableRootDirectory 'IGoLibrary-Ex.exe')
    foreach ($item in Get-ChildItem -LiteralPath $AppSource -Force) {
        $copyParameters = @{
            LiteralPath = $item.FullName
            Destination = $appDestination
            Recurse = $true
            Force = $true
            ErrorAction = 'Stop'
        }
        Copy-Item @copyParameters
    }
}

function Install-ValidatedReleaseArtifacts {
    param(
        [Parameter(Mandatory)][string]$StagedLightweightPath,
        [Parameter(Mandatory)][string]$StagedBundledPath,
        [Parameter(Mandatory)][string]$StagedPortableLightweightPath,
        [Parameter(Mandatory)][string]$StagedPortableBundledPath,
        [Parameter(Mandatory)][string]$StagedSymbolsPath,
        [Parameter(Mandatory)][string]$FinalLightweightPath,
        [Parameter(Mandatory)][string]$FinalBundledPath,
        [Parameter(Mandatory)][string]$FinalPortableLightweightPath,
        [Parameter(Mandatory)][string]$FinalPortableBundledPath,
        [Parameter(Mandatory)][string]$LegacyBundledPath,
        [Parameter(Mandatory)][string]$FinalSymbolsPath
    )

    $previousLightweightPath = Join-Path $packageStaging '.previous-lightweight.zip'
    $previousBundledPath = Join-Path $packageStaging '.previous-bundled.zip'
    $previousPortableLightweightPath = Join-Path $packageStaging '.previous-portable-lightweight.zip'
    $previousPortableBundledPath = Join-Path $packageStaging '.previous-portable-bundled.zip'
    $previousLegacyBundledPath = Join-Path $packageStaging '.previous-legacy-with-cloudflared.zip'
    $previousSymbolsPath = Join-Path $packageStaging '.previous-symbols'
    $lightweightBackedUp = $false
    $bundledBackedUp = $false
    $portableLightweightBackedUp = $false
    $portableBundledBackedUp = $false
    $legacyBundledBackedUp = $false
    $symbolsBackedUp = $false
    $lightweightInstalled = $false
    $bundledInstalled = $false
    $portableLightweightInstalled = $false
    $portableBundledInstalled = $false
    $symbolsInstalled = $false

    $symbolsParent = [System.IO.Path]::GetDirectoryName($FinalSymbolsPath)
    if ([string]::IsNullOrWhiteSpace($symbolsParent)) {
        throw "无法确定符号归档父目录：$FinalSymbolsPath"
    }
    New-Item -ItemType Directory -Path $symbolsParent -Force | Out-Null

    try {
        if (Test-Path -LiteralPath $FinalLightweightPath) {
            Move-Item -LiteralPath $FinalLightweightPath -Destination $previousLightweightPath
            $lightweightBackedUp = $true
        }
        if (Test-Path -LiteralPath $FinalBundledPath) {
            Move-Item -LiteralPath $FinalBundledPath -Destination $previousBundledPath
            $bundledBackedUp = $true
        }
        if (Test-Path -LiteralPath $FinalPortableLightweightPath) {
            Move-Item -LiteralPath $FinalPortableLightweightPath -Destination $previousPortableLightweightPath
            $portableLightweightBackedUp = $true
        }
        if (Test-Path -LiteralPath $FinalPortableBundledPath) {
            Move-Item -LiteralPath $FinalPortableBundledPath -Destination $previousPortableBundledPath
            $portableBundledBackedUp = $true
        }
        if (Test-Path -LiteralPath $LegacyBundledPath) {
            Move-Item -LiteralPath $LegacyBundledPath -Destination $previousLegacyBundledPath
            $legacyBundledBackedUp = $true
        }
        if (Test-Path -LiteralPath $FinalSymbolsPath) {
            Move-Item -LiteralPath $FinalSymbolsPath -Destination $previousSymbolsPath
            $symbolsBackedUp = $true
        }

        Move-Item -LiteralPath $StagedLightweightPath -Destination $FinalLightweightPath
        $lightweightInstalled = $true
        Move-Item -LiteralPath $StagedBundledPath -Destination $FinalBundledPath
        $bundledInstalled = $true
        Move-Item -LiteralPath $StagedPortableLightweightPath -Destination $FinalPortableLightweightPath
        $portableLightweightInstalled = $true
        Move-Item -LiteralPath $StagedPortableBundledPath -Destination $FinalPortableBundledPath
        $portableBundledInstalled = $true
        Move-Item -LiteralPath $StagedSymbolsPath -Destination $FinalSymbolsPath
        $symbolsInstalled = $true
    }
    catch {
        if ($lightweightInstalled -and (Test-Path -LiteralPath $FinalLightweightPath)) {
            Remove-Item -LiteralPath $FinalLightweightPath -Force
        }
        if ($bundledInstalled -and (Test-Path -LiteralPath $FinalBundledPath)) {
            Remove-Item -LiteralPath $FinalBundledPath -Force
        }
        if ($portableLightweightInstalled -and (Test-Path -LiteralPath $FinalPortableLightweightPath)) {
            Remove-Item -LiteralPath $FinalPortableLightweightPath -Force
        }
        if ($portableBundledInstalled -and (Test-Path -LiteralPath $FinalPortableBundledPath)) {
            Remove-Item -LiteralPath $FinalPortableBundledPath -Force
        }
        if ($symbolsInstalled -and (Test-Path -LiteralPath $FinalSymbolsPath)) {
            Remove-SafeBuildDirectory -Path $FinalSymbolsPath
        }
        if ($lightweightBackedUp -and (Test-Path -LiteralPath $previousLightweightPath)) {
            Move-Item -LiteralPath $previousLightweightPath -Destination $FinalLightweightPath
        }
        if ($bundledBackedUp -and (Test-Path -LiteralPath $previousBundledPath)) {
            Move-Item -LiteralPath $previousBundledPath -Destination $FinalBundledPath
        }
        if ($portableLightweightBackedUp -and (Test-Path -LiteralPath $previousPortableLightweightPath)) {
            Move-Item -LiteralPath $previousPortableLightweightPath -Destination $FinalPortableLightweightPath
        }
        if ($portableBundledBackedUp -and (Test-Path -LiteralPath $previousPortableBundledPath)) {
            Move-Item -LiteralPath $previousPortableBundledPath -Destination $FinalPortableBundledPath
        }
        if ($legacyBundledBackedUp -and (Test-Path -LiteralPath $previousLegacyBundledPath)) {
            Move-Item -LiteralPath $previousLegacyBundledPath -Destination $LegacyBundledPath
        }
        if ($symbolsBackedUp -and (Test-Path -LiteralPath $previousSymbolsPath)) {
            Move-Item -LiteralPath $previousSymbolsPath -Destination $FinalSymbolsPath
        }
        throw
    }

    foreach ($backupPath in @(
        $previousLightweightPath,
        $previousBundledPath,
        $previousPortableLightweightPath,
        $previousPortableBundledPath,
        $previousLegacyBundledPath)) {
        if (Test-Path -LiteralPath $backupPath) {
            Remove-Item -LiteralPath $backupPath -Force
        }
    }
    if (Test-Path -LiteralPath $previousSymbolsPath) {
        Remove-SafeBuildDirectory -Path $previousSymbolsPath
    }
}

Assert-NativeAotToolchain
$launcherTestParameters = @{
    TestProjectPath = $launcherTestsProject
    BuildConfiguration = $Configuration
}
Assert-LauncherUnitTests @launcherTestParameters

Remove-SafeBuildDirectory -Path $output
Remove-SafeBuildDirectory -Path $updaterOutput
Remove-SafeBuildDirectory -Path $launcherOutput
Remove-SafeBuildDirectory -Path $lightweightStaging
Remove-SafeBuildDirectory -Path $portableStaging
Remove-SafeBuildDirectory -Path $packageStaging
New-Item -ItemType Directory -Path $output -Force | Out-Null
New-Item -ItemType Directory -Path $updaterOutput -Force | Out-Null
New-Item -ItemType Directory -Path $launcherOutput -Force | Out-Null
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
    '-p:PublishAot=true'
    '-p:OptimizationPreference=Size'
    '-p:ILLinkTreatWarningsAsErrors=true'
    '-p:IlcTreatWarningsAsErrors=true'
    '-p:UseSharedCompilation=false'
    "-p:Version=$AppVersion"
    "-p:FileVersion=${AppVersion}.0"
    "-p:InformationalVersion=$AppVersion"
    '-p:IncludeSourceRevisionInInformationalVersion=false'
    '-o'
    $updaterOutput
)
& dotnet @updaterPublishArguments
$updaterPublishExitCode = $LASTEXITCODE
if ($updaterPublishExitCode -ne 0) {
    throw "Updater dotnet publish failed with exit code $updaterPublishExitCode."
}

$launcherPublishArguments = @(
    'publish'
    $launcherProject
    '-c'
    $Configuration
    '-r'
    $Runtime
    '--self-contained:true'
    '-p:PublishAot=true'
    '-p:OptimizationPreference=Size'
    '-p:ILLinkTreatWarningsAsErrors=true'
    '-p:IlcTreatWarningsAsErrors=true'
    '-p:UseSharedCompilation=false'
    "-p:Version=$AppVersion"
    "-p:FileVersion=${AppVersion}.0"
    "-p:InformationalVersion=$AppVersion"
    '-p:IncludeSourceRevisionInInformationalVersion=false'
    '-o'
    $launcherOutput
)
& dotnet @launcherPublishArguments
$launcherPublishExitCode = $LASTEXITCODE
if ($launcherPublishExitCode -ne 0) {
    throw "Launcher dotnet publish failed with exit code $launcherPublishExitCode."
}

$publishedExecutable = Join-Path $output 'IGoLibrary.Ex.Desktop.exe'
$publishedUpdater = Join-Path $updaterOutput 'IGoLibrary.Ex.Updater.exe'
$publishedLauncher = Join-Path $launcherOutput 'IGoLibrary-Ex.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published executable was not found: $publishedExecutable"
}
if (-not (Test-Path -LiteralPath $publishedUpdater -PathType Leaf)) {
    throw "Published updater was not found: $publishedUpdater"
}
$launcherVerificationParameters = @{
    ExecutablePath = $publishedLauncher
    OutputDirectory = $launcherOutput
}
Assert-PublishedLauncherBinary @launcherVerificationParameters
$publishedUpdaterInfo = Get-Item -LiteralPath $publishedUpdater
if ($publishedUpdaterInfo.Length -gt $maximumUpdaterBytes) {
    throw "Native AOT updater 超过 20 MiB 门槛：$($publishedUpdaterInfo.Length) bytes。"
}

$unexpectedUpdaterSidecars = @(
    Get-ChildItem -LiteralPath $updaterOutput -File |
        Where-Object {
            $_.Extension -in @('.dll', '.json') -or
            $_.Name.EndsWith('.deps.json', [System.StringComparison]::OrdinalIgnoreCase) -or
            $_.Name.EndsWith('.runtimeconfig.json', [System.StringComparison]::OrdinalIgnoreCase)
        }
)
if ($unexpectedUpdaterSidecars.Count -gt 0) {
    throw "Native AOT updater 发布输出包含托管 sidecar：$($unexpectedUpdaterSidecars.Name -join ', ')"
}

$versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($publishedUpdater)
$expectedFileVersion = [version]::Parse("${AppVersion}.0")
$actualFileVersion = [version]::Parse($versionInfo.FileVersion)
if ($actualFileVersion -ne $expectedFileVersion -or
    $versionInfo.ProductVersion -cne $AppVersion) {
    throw "Updater 版本资源不匹配。FileVersion=$($versionInfo.FileVersion)，ProductVersion=$($versionInfo.ProductVersion)，期望=$AppVersion。"
}

Assert-UpdaterHeadlessSmoke -ExecutablePath $publishedUpdater
Assert-UpdaterTaskDialogSmoke -ExecutablePath $publishedUpdater
$acceptanceParameters = @{
    AotUpdaterPath = $publishedUpdater
    ManagedBaselinePath = $ManagedUpdaterBaselinePath
    TestProjectPath = $updaterAcceptanceProject
    BuildConfiguration = $Configuration
}
Assert-PublishedUpdaterTransactions @acceptanceParameters
$launcherSmokeParameters = @{
    ExecutablePath = $publishedLauncher
    BuildConfiguration = $Configuration
}
Assert-PublishedLauncherSmoke @launcherSmokeParameters

$primarySymbols = Join-Path $updaterOutput 'IGoLibrary.Ex.Updater.pdb'
if (-not (Test-Path -LiteralPath $primarySymbols -PathType Leaf)) {
    throw "Native AOT updater 缺少内部故障诊断所需的 PDB：$primarySymbols"
}
$primaryLauncherSymbols = Join-Path $launcherOutput 'IGoLibrary-Ex.pdb'
if (-not (Test-Path -LiteralPath $primaryLauncherSymbols -PathType Leaf)) {
    throw "Native AOT launcher 缺少内部故障诊断所需的 PDB：$primaryLauncherSymbols"
}
$publishedSymbols = @(
    Get-ChildItem -LiteralPath $updaterOutput -File -Filter '*.pdb'
    Get-ChildItem -LiteralPath $launcherOutput -File -Filter '*.pdb'
)
Copy-Item -LiteralPath $publishedUpdater -Destination (Join-Path $output 'IGoLibrary.Ex.Updater.exe') -Force

$portableMarkerPath = Join-Path $output 'portable-release.marker'
[System.IO.File]::WriteAllText(
    $portableMarkerPath,
    'IGoLibrary-Ex|portable|win-x64|2',
    [System.Text.UTF8Encoding]::new($false))

$prepareCloudflared = Join-Path $PSScriptRoot 'prepare-cloudflared.ps1'
$cloudflaredDestination = Join-Path $output 'tools\cloudflared'
& $prepareCloudflared -Runtime $Runtime -DestinationDirectory $cloudflaredDestination

try {
    Copy-DirectoryWithoutTools -Source $output -Destination $lightweightStaging
    Write-UpdateManifest -RootDirectory $lightweightStaging -IncludeCloudflared $false
    Write-UpdateManifest -RootDirectory $output -IncludeCloudflared $true
    New-Item -ItemType Directory -Path $packageStaging -Force | Out-Null
    New-Item -ItemType Directory -Path $stagedSymbolsOutput -Force | Out-Null
    foreach ($symbol in $publishedSymbols) {
        Copy-Item -LiteralPath $symbol.FullName -Destination $stagedSymbolsOutput -Force
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $lightweightStaging,
        $stagedZipPath,
        [System.IO.Compression.CompressionLevel]::SmallestSize,
        $false)
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $output,
        $stagedBundledZipPath,
        [System.IO.Compression.CompressionLevel]::SmallestSize,
        $false)

    $verifyScript = Join-Path $PSScriptRoot 'verify-windows-package.ps1'
    & $verifyScript -PackagePath $stagedZipPath -CompanionPackagePath $stagedBundledZipPath

    $portableLightweightParameters = @{
        AppSource = $lightweightStaging
        Destination = $portableLightweightStaging
        LauncherPath = $publishedLauncher
    }
    New-PortablePackageTree @portableLightweightParameters
    $portableBundledParameters = @{
        AppSource = $output
        Destination = $portableBundledStaging
        LauncherPath = $publishedLauncher
    }
    New-PortablePackageTree @portableBundledParameters
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $portableLightweightStaging,
        $stagedPortableZipPath,
        [System.IO.Compression.CompressionLevel]::SmallestSize,
        $false)
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $portableBundledStaging,
        $stagedPortableBundledZipPath,
        [System.IO.Compression.CompressionLevel]::SmallestSize,
        $false)

    $portableVerifyScript = Join-Path $PSScriptRoot 'verify-windows-portable-package.ps1'
    $portableVerificationParameters = @{
        PackagePath = $stagedPortableZipPath
        RawPackagePath = $stagedZipPath
        CompanionPackagePath = $stagedPortableBundledZipPath
        CompanionRawPackagePath = $stagedBundledZipPath
        PublishedLauncherPath = $publishedLauncher
    }
    & $portableVerifyScript @portableVerificationParameters
    $portableVerifierTestScript = Join-Path $PSScriptRoot 'test-windows-portable-package-verifier.ps1'
    $portableVerifierTestParameters = @{
        PublishedLauncherPath = $publishedLauncher
        AppVersion = $AppVersion
    }
    & $portableVerifierTestScript @portableVerifierTestParameters

    $installParameters = @{
        StagedLightweightPath = $stagedZipPath
        StagedBundledPath = $stagedBundledZipPath
        StagedPortableLightweightPath = $stagedPortableZipPath
        StagedPortableBundledPath = $stagedPortableBundledZipPath
        StagedSymbolsPath = $stagedSymbolsOutput
        FinalLightweightPath = $zipPath
        FinalBundledPath = $bundledZipPath
        FinalPortableLightweightPath = $portableZipPath
        FinalPortableBundledPath = $portableBundledZipPath
        LegacyBundledPath = $legacyBundledZipPath
        FinalSymbolsPath = $symbolsOutput
    }
    Install-ValidatedReleaseArtifacts @installParameters
}
finally {
    Remove-SafeBuildDirectory -Path $lightweightStaging
    Remove-SafeBuildDirectory -Path $portableStaging
    Remove-SafeBuildDirectory -Path $packageStaging
}

Write-Host "Published complete desktop tree to $output"
foreach ($package in @(
    [pscustomobject]@{ Label = 'Windows recommended portable ZIP'; Path = $portableBundledZipPath },
    [pscustomobject]@{ Label = 'Windows portable without-cloudflared ZIP'; Path = $portableZipPath },
    [pscustomobject]@{ Label = 'Windows automatic-update payload ZIP'; Path = $bundledZipPath },
    [pscustomobject]@{ Label = 'Windows legacy-compatible without-cloudflared payload ZIP'; Path = $zipPath }
)) {
    $packageInfo = Get-Item -LiteralPath $package.Path
    $packageHash = (Get-FileHash -LiteralPath $package.Path -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host "$($package.Label): $($package.Path)"
    Write-Host "  Size: $($packageInfo.Length) bytes"
    Write-Host "  SHA-256: $packageHash"
}
$lightweightArchive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $updaterEntry = $lightweightArchive.GetEntry('IGoLibrary.Ex.Updater.exe')
    if ($null -eq $updaterEntry) {
        throw '轻量包缺少 IGoLibrary.Ex.Updater.exe。'
    }

    if ($updaterEntry.CompressedLength -gt $maximumUpdaterCompressedBytes) {
        throw "Native AOT updater ZIP 条目超过 10 MiB 门槛：$($updaterEntry.CompressedLength) bytes。"
    }

    $lightweightPackageBytes = (Get-Item -LiteralPath $zipPath).Length
    $packageShare = if ($lightweightPackageBytes -eq 0) {
        0
    }
    else {
        [Math]::Round(($updaterEntry.CompressedLength / $lightweightPackageBytes) * 100, 2)
    }
    Write-Host 'Updater size budget:'
    Write-Host "  Raw: $($updaterEntry.Length) / $maximumUpdaterBytes bytes"
    Write-Host "  Compressed: $($updaterEntry.CompressedLength) / $maximumUpdaterCompressedBytes bytes"
    Write-Host "  Lightweight ZIP share: $packageShare%"
}
finally {
    $lightweightArchive.Dispose()
}
$portableArchive = [System.IO.Compression.ZipFile]::OpenRead($portableZipPath)
try {
    $launcherEntry = $portableArchive.GetEntry('IGoLibrary-Ex/IGoLibrary-Ex.exe')
    if ($null -eq $launcherEntry) {
        throw 'portable 轻量包缺少 IGoLibrary-Ex/IGoLibrary-Ex.exe。'
    }
    if ($launcherEntry.CompressedLength -gt $maximumLauncherCompressedBytes) {
        throw "Native AOT launcher ZIP 条目超过 5 MiB 门槛：$($launcherEntry.CompressedLength) bytes。"
    }

    Write-Host 'Launcher size budget:'
    Write-Host "  Raw: $($launcherEntry.Length) / $maximumLauncherBytes bytes"
    Write-Host "  Compressed: $($launcherEntry.CompressedLength) / $maximumLauncherCompressedBytes bytes"
}
finally {
    $portableArchive.Dispose()
}
Write-Host '普通 Windows 用户应下载 -portable.zip 并运行外层 IGoLibrary-Ex.exe；无后缀完整包仍是唯一的应用内自动更新资产。'
