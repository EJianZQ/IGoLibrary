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
        try
        {
            var (exitCode, _) = RunRegProcess($"query \"{WindowsRunKey}\" /v {AppName}", redirectError: false);
            return exitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void EnableWindowsStartupEntry()
    {
        var exePath = GetExecutablePath()
            ?? throw new InvalidOperationException("无法确定当前可执行文件路径");

        var quotedPath = "\"" + exePath + "\"";
        var (exitCode, stderr) = RunRegProcess(
            $"add \"{WindowsRunKey}\" /v {AppName} /t REG_SZ /d {quotedPath} /f",
            redirectError: true);

        if (exitCode != 0)
        {
            throw new InvalidOperationException(
                $"写入开机启动注册表失败（退出码 {exitCode}）：{stderr.Trim()}");
        }
    }

    private static void DisableWindowsStartupEntry()
    {
        try
        {
            RunRegProcess($"delete \"{WindowsRunKey}\" /v {AppName} /f", redirectError: false);
            // ExitCode 1 means the value doesn't exist — that's fine when disabling.
        }
        catch
        {
            // Ignore errors when removing a non-existent entry.
        }
    }

    /// <summary>
    /// Runs reg.exe with the given arguments, reading stderr asynchronously to avoid deadlocks.
    /// Returns (exitCode, stderr). Throws if the process cannot be started or times out.
    /// </summary>
    private static (int ExitCode, string StdError) RunRegProcess(string arguments, bool redirectError)
    {
        var info = new ProcessStartInfo("reg", arguments)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = redirectError
        };

        var process = Process.Start(info)
            ?? throw new InvalidOperationException("无法启动 reg.exe 进程");

        // Read stderr asynchronously BEFORE WaitForExit to prevent deadlocks
        // when the child process fills its stderr pipe buffer.
        var stderrTask = redirectError
            ? process.StandardError.ReadToEndAsync()
            : Task.FromResult(string.Empty);

        if (!process.WaitForExit(ProcessTimeout))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"reg.exe 在 {ProcessTimeout.TotalSeconds:0}s 内未退出");
        }

        var stderr = stderrTask.IsCompleted
            ? stderrTask.Result
            : string.Empty;

        return (process.ExitCode, stderr);
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
