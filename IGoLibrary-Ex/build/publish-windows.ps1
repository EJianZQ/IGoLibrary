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
$symbolsOutput = [System.IO.Path]::GetFullPath((Join-Path $artifactsRoot "symbols\$Runtime\v$AppVersion"))
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

function Install-ValidatedReleaseArtifacts {
    param(
        [Parameter(Mandatory)][string]$StagedLightweightPath,
        [Parameter(Mandatory)][string]$StagedBundledPath,
        [Parameter(Mandatory)][string]$StagedSymbolsPath,
        [Parameter(Mandatory)][string]$FinalLightweightPath,
        [Parameter(Mandatory)][string]$FinalBundledPath,
        [Parameter(Mandatory)][string]$FinalSymbolsPath
    )

    $previousLightweightPath = Join-Path $packageStaging '.previous-lightweight.zip'
    $previousBundledPath = Join-Path $packageStaging '.previous-bundled.zip'
    $previousSymbolsPath = Join-Path $packageStaging '.previous-symbols'
    $lightweightBackedUp = $false
    $bundledBackedUp = $false
    $symbolsBackedUp = $false
    $lightweightInstalled = $false
    $bundledInstalled = $false
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
        if (Test-Path -LiteralPath $FinalSymbolsPath) {
            Move-Item -LiteralPath $FinalSymbolsPath -Destination $previousSymbolsPath
            $symbolsBackedUp = $true
        }

        Move-Item -LiteralPath $StagedLightweightPath -Destination $FinalLightweightPath
        $lightweightInstalled = $true
        Move-Item -LiteralPath $StagedBundledPath -Destination $FinalBundledPath
        $bundledInstalled = $true
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
        if ($symbolsInstalled -and (Test-Path -LiteralPath $FinalSymbolsPath)) {
            Remove-SafeBuildDirectory -Path $FinalSymbolsPath
        }
        if ($lightweightBackedUp -and (Test-Path -LiteralPath $previousLightweightPath)) {
            Move-Item -LiteralPath $previousLightweightPath -Destination $FinalLightweightPath
        }
        if ($bundledBackedUp -and (Test-Path -LiteralPath $previousBundledPath)) {
            Move-Item -LiteralPath $previousBundledPath -Destination $FinalBundledPath
        }
        if ($symbolsBackedUp -and (Test-Path -LiteralPath $previousSymbolsPath)) {
            Move-Item -LiteralPath $previousSymbolsPath -Destination $FinalSymbolsPath
        }
        throw
    }

    foreach ($backupPath in @($previousLightweightPath, $previousBundledPath)) {
        if (Test-Path -LiteralPath $backupPath) {
            Remove-Item -LiteralPath $backupPath -Force
        }
    }
    if (Test-Path -LiteralPath $previousSymbolsPath) {
        Remove-SafeBuildDirectory -Path $previousSymbolsPath
    }
}

Assert-NativeAotToolchain

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

$publishedExecutable = Join-Path $output 'IGoLibrary.Ex.Desktop.exe'
$publishedUpdater = Join-Path $updaterOutput 'IGoLibrary.Ex.Updater.exe'
if (-not (Test-Path -LiteralPath $publishedExecutable -PathType Leaf)) {
    throw "Published executable was not found: $publishedExecutable"
}
if (-not (Test-Path -LiteralPath $publishedUpdater -PathType Leaf)) {
    throw "Published updater was not found: $publishedUpdater"
}
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

$primarySymbols = Join-Path $updaterOutput 'IGoLibrary.Ex.Updater.pdb'
if (-not (Test-Path -LiteralPath $primarySymbols -PathType Leaf)) {
    throw "Native AOT updater 缺少内部故障诊断所需的 PDB：$primarySymbols"
}
$publishedSymbols = @(Get-ChildItem -LiteralPath $updaterOutput -File -Filter '*.pdb')
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

    $installParameters = @{
        StagedLightweightPath = $stagedZipPath
        StagedBundledPath = $stagedBundledZipPath
        StagedSymbolsPath = $stagedSymbolsOutput
        FinalLightweightPath = $zipPath
        FinalBundledPath = $bundledZipPath
        FinalSymbolsPath = $symbolsOutput
    }
    Install-ValidatedReleaseArtifacts @installParameters
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
Write-Host '无后缀轻量包是唯一的应用内自动更新资产；首次启用自动更新仍需手动安装一次绿色版。'
