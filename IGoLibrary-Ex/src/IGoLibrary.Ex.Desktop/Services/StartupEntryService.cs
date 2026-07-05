using System.Diagnostics;
using System.Text;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class StartupEntryService : IStartupEntryService
{
    private const string AppName = "IGoLibrary-Ex";
    private const string WindowsRunKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run";
    private const string MacLaunchAgentPlist = "com.IGoLibrary-Ex.plist";
    private static readonly TimeSpan ProcessTimeout = TimeSpan.FromSeconds(10);

    public bool IsSupported => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS();

    public Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (OperatingSystem.IsWindows())
        {
            return Task.FromResult(IsWindowsStartupEntryEnabled());
        }

        if (OperatingSystem.IsMacOS())
        {
            return Task.FromResult(IsMacLaunchAgentEnabled());
        }

        return Task.FromResult(false);
    }

    public Task EnableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (OperatingSystem.IsWindows())
        {
            EnableWindowsStartupEntry();
            return Task.CompletedTask;
        }

        if (OperatingSystem.IsMacOS())
        {
            EnableMacLaunchAgent();
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    public Task DisableAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (OperatingSystem.IsWindows())
        {
            DisableWindowsStartupEntry();
            return Task.CompletedTask;
        }

        if (OperatingSystem.IsMacOS())
        {
            DisableMacLaunchAgent();
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    private static string? GetExecutablePath()
    {
        return Environment.ProcessPath;
    }

    // ── Windows (registry via reg.exe) ──────────────────────────────────

    private static bool IsWindowsStartupEntryEnabled()
    {
        return TryQueryWindowsStartupEntry(out var exists, out _) && exists;
    }

    private static void EnableWindowsStartupEntry()
    {
        var exePath = GetExecutablePath()
            ?? throw new InvalidOperationException("无法确定当前可执行文件路径");

        var quotedPath = "\"" + exePath + "\"";
        var (exitCode, _, stderr) = RunRegProcess(
            $"add \"{WindowsRunKey}\" /v {AppName} /t REG_SZ /d {quotedPath} /f",
            redirectError: true);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"写入开机启动注册表失败（退出码 {exitCode}）：{FormatProcessError(stderr)}");
        }
    }

    private static void DisableWindowsStartupEntry()
    {
        var (exitCode, _, stderr) = RunRegProcess(
            $"delete \"{WindowsRunKey}\" /v {AppName} /f",
            redirectError: true);

        if (exitCode == 0)
        {
            return;
        }

        if (TryQueryWindowsStartupEntry(out var exists, out var queryError) && !exists)
        {
            return;
        }

        var detail = string.IsNullOrWhiteSpace(stderr)
            ? queryError
            : stderr;
        throw new InvalidOperationException(
            $"移除开机启动注册表失败（退出码 {exitCode}）：{FormatProcessError(detail)}");
    }

    private static bool TryQueryWindowsStartupEntry(out bool exists, out string error)
    {
        try
        {
            var (exitCode, _, stderr) = RunRegProcess(
                $"query \"{WindowsRunKey}\" /v {AppName}",
                redirectError: true);
            exists = exitCode == 0;
            error = stderr;
            return true;
        }
        catch (Exception ex)
        {
            exists = false;
            error = ex.Message;
            return false;
        }
    }

    /// <summary>
    /// Runs reg.exe with the given arguments, reading stdout/stderr asynchronously to avoid deadlocks.
    /// Returns (exitCode, stdout, stderr). Throws if the process cannot be started or times out.
    /// </summary>
    private static (int ExitCode, string StdOut, string StdError) RunRegProcess(string arguments, bool redirectError)
    {
        var info = new ProcessStartInfo("reg", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = redirectError
        };

        using var process = Process.Start(info)
            ?? throw new InvalidOperationException("无法启动 reg.exe 进程");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = redirectError
            ? process.StandardError.ReadToEndAsync()
            : Task.FromResult(string.Empty);

        if (!process.WaitForExit(ProcessTimeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"reg.exe 在 {ProcessTimeout.TotalSeconds:0}s 内未退出");
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        return (process.ExitCode, stdout, stderr);
    }

    private static string FormatProcessError(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "未返回错误详情"
            : value.Trim();
    }

    // ── macOS (LaunchAgent plist) ───────────────────────────────────────

    private static string GetMacLaunchAgentPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, "Library", "LaunchAgents", MacLaunchAgentPlist);
    }

    private static bool IsMacLaunchAgentEnabled()
    {
        return File.Exists(GetMacLaunchAgentPath());
    }

    private static void EnableMacLaunchAgent()
    {
        var exePath = GetExecutablePath()
            ?? throw new InvalidOperationException("无法确定当前可执行文件路径");

        var plistPath = GetMacLaunchAgentPath();
        var directory = Path.GetDirectoryName(plistPath);
        if (directory is not null && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var plist = BuildMacLaunchAgentPlist(exePath);
        File.WriteAllText(plistPath, plist);
    }

    private static void DisableMacLaunchAgent()
    {
        var plistPath = GetMacLaunchAgentPath();
        if (File.Exists(plistPath))
        {
            File.Delete(plistPath);
        }
    }

    private static string BuildMacLaunchAgentPlist(string exePath)
    {
        // Escape XML special characters to prevent injection via executable path.
        var escapedExePath = System.Security.SecurityElement.Escape(exePath)
            ?? exePath;

        var builder = new StringBuilder();
        builder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        builder.AppendLine("<!DOCTYPE plist PUBLIC \"-//Apple//DTD PLIST 1.0//EN\" \"http://www.apple.com/DTDs/PropertyList-1.0.dtd\">");
        builder.AppendLine("<plist version=\"1.0\">");
        builder.AppendLine("<dict>");
        builder.AppendLine($"  <key>Label</key>");
        builder.AppendLine($"  <string>{AppName}</string>");
        builder.AppendLine($"  <key>ProgramArguments</key>");
        builder.AppendLine($"  <array>");
        builder.AppendLine($"    <string>{escapedExePath}</string>");
        builder.AppendLine($"  </array>");
        builder.AppendLine($"  <key>RunAtLoad</key>");
        builder.AppendLine($"  <true/>");
        builder.AppendLine("</dict>");
        builder.AppendLine("</plist>");
        return builder.ToString();
    }
}
