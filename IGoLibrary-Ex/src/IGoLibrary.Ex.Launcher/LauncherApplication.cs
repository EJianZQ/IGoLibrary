using System.Diagnostics;

namespace IGoLibrary.Ex.Launcher;

internal static class LauncherApplication
{
    internal const int SuccessExitCode = 0;
    internal const int DeploymentFailureExitCode = 2;
    internal const int StartFailureExitCode = 3;

    private const string AppDirectoryName = "app";
    private const string EntryExecutableName = "IGoLibrary.Ex.Desktop.exe";

    public static int Run(IReadOnlyList<string> arguments, ILauncherPlatform platform)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(platform);

        string appDirectory;
        string entryExecutable;
        try
        {
            var processPath = platform.ProcessPath;
            if (string.IsNullOrWhiteSpace(processPath) ||
                !Path.IsPathFullyQualified(processPath))
            {
                ShowDeploymentFailure(platform, null);
                return DeploymentFailureExitCode;
            }

            var launcherDirectory = Path.GetDirectoryName(processPath);
            if (string.IsNullOrWhiteSpace(launcherDirectory))
            {
                ShowDeploymentFailure(platform, null);
                return DeploymentFailureExitCode;
            }

            appDirectory = Path.GetFullPath(Path.Combine(launcherDirectory, AppDirectoryName));
            entryExecutable = Path.GetFullPath(Path.Combine(appDirectory, EntryExecutableName));
        }
        catch (Exception exception) when (IsPathException(exception))
        {
            ShowDeploymentFailure(platform, null);
            return DeploymentFailureExitCode;
        }

        if (!platform.DirectoryExists(appDirectory) ||
            !platform.FileExists(entryExecutable))
        {
            ShowDeploymentFailure(platform, entryExecutable);
            return DeploymentFailureExitCode;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = entryExecutable,
                WorkingDirectory = appDirectory,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (!platform.TryStart(startInfo))
            {
                ShowStartFailure(platform, "Windows 未能创建应用进程。");
                return StartFailureExitCode;
            }

            return SuccessExitCode;
        }
        catch (Exception exception)
        {
            ShowStartFailure(platform, GetSafeErrorMessage(exception));
            return StartFailureExitCode;
        }
    }

    private static bool IsPathException(Exception exception)
    {
        return exception is ArgumentException or NotSupportedException or PathTooLongException;
    }

    private static string GetSafeErrorMessage(Exception exception)
    {
        var message = exception.Message.Trim();
        if (message.Length == 0)
        {
            return "系统未提供详细原因。";
        }

        const int maximumLength = 400;
        return message.Length <= maximumLength
            ? message
            : message[..maximumLength] + "…";
    }

    private static void ShowDeploymentFailure(
        ILauncherPlatform platform,
        string? expectedExecutable)
    {
        var location = expectedExecutable is null
            ? "无法确定启动器所在目录。"
            : $"未找到程序文件：\n{expectedExecutable}";
        TryShowError(
            platform,
            $"{location}\n\n请完整解压整个压缩包，不要只复制 IGoLibrary-Ex.exe。\n" +
            "如果文件曾经存在，请检查 Windows 安全中心或其他安全软件的隔离记录。");
    }

    private static void ShowStartFailure(ILauncherPlatform platform, string detail)
    {
        TryShowError(
            platform,
            $"无法启动“我去图书馆”。\n\n{detail}\n\n" +
            "请确认压缩包已完整解压，并检查当前用户是否拥有运行权限。");
    }

    private static void TryShowError(ILauncherPlatform platform, string message)
    {
        try
        {
            platform.ShowError(message);
        }
        catch
        {
            // The launcher must still return its documented exit code if UI reporting fails.
        }
    }
}
