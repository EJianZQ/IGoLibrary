using System.Diagnostics;

namespace IGoLibrary.Ex.Updater.Core;

public static class UpdateProcessStartInfoFactory
{
    public static ProcessStartInfo CreateWorker(
        string updaterExecutablePath,
        string requestPath)
    {
        var executable = Path.GetFullPath(updaterExecutablePath);
        var request = Path.GetFullPath(requestPath);
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)
                               ?? throw new InvalidOperationException("无法确定更新程序目录"),
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

        startInfo.ArgumentList.Add("--worker");
        startInfo.ArgumentList.Add("--request");
        startInfo.ArgumentList.Add(request);
        return startInfo;
    }

    public static ProcessStartInfo CreateBootstrap(
        string trustedUpdaterExecutablePath,
        string pipeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pipeName);
        if (pipeName.Any(static character =>
                !(char.IsAsciiLetterOrDigit(character) || character is '.' or '-')))
        {
            throw new ArgumentException("更新命名管道名称无效", nameof(pipeName));
        }

        var executable = Path.GetFullPath(trustedUpdaterExecutablePath);
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = true,
            Verb = "runas",
            WorkingDirectory = Path.GetDirectoryName(executable)
                               ?? throw new InvalidOperationException("无法确定更新程序目录"),
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--bootstrap");
        startInfo.ArgumentList.Add("--pipe");
        startInfo.ArgumentList.Add(pipeName);
        return startInfo;
    }

    public static ProcessStartInfo CreateRecoveryWorker(
        string updaterExecutablePath,
        string requestPath,
        bool elevate,
        int recoveryCoordinatorProcessId)
    {
        if (recoveryCoordinatorProcessId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recoveryCoordinatorProcessId));
        }

        var executable = Path.GetFullPath(updaterExecutablePath);
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = elevate,
            WorkingDirectory = Path.GetDirectoryName(executable)
                               ?? throw new InvalidOperationException("无法确定恢复程序目录"),
            CreateNoWindow = !elevate,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        if (elevate)
        {
            startInfo.Verb = "runas";
        }

        startInfo.ArgumentList.Add("--recover-worker");
        startInfo.ArgumentList.Add("--request");
        startInfo.ArgumentList.Add(Path.GetFullPath(requestPath));
        startInfo.ArgumentList.Add("--recovery-coordinator-pid");
        startInfo.ArgumentList.Add(recoveryCoordinatorProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return startInfo;
    }

    public static ProcessStartInfo CreateRecoveryCoordinator(
        string updaterExecutablePath,
        string requestPath)
    {
        var executable = Path.GetFullPath(updaterExecutablePath);
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)
                               ?? throw new InvalidOperationException("无法确定恢复程序目录"),
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--recover");
        startInfo.ArgumentList.Add("--request");
        startInfo.ArgumentList.Add(Path.GetFullPath(requestPath));
        return startInfo;
    }

    public static ProcessStartInfo CreateCleanup(
        string installedUpdaterExecutablePath,
        string requestPath,
        IEnumerable<int> processIdsToWaitFor)
    {
        var executable = Path.GetFullPath(installedUpdaterExecutablePath);
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(executable)
                               ?? throw new InvalidOperationException("无法确定清理程序目录"),
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        startInfo.ArgumentList.Add("--cleanup");
        startInfo.ArgumentList.Add("--request");
        startInfo.ArgumentList.Add(Path.GetFullPath(requestPath));
        foreach (var processId in processIdsToWaitFor.Distinct())
        {
            if (processId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(processIdsToWaitFor));
            }

            startInfo.ArgumentList.Add("--wait-pid");
            startInfo.ArgumentList.Add(processId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return startInfo;
    }

    public static ProcessStartInfo CreateApplication(
        string executablePath,
        string workingDirectory,
        string? updateTransactionId)
    {
        var startInfo = new ProcessStartInfo(Path.GetFullPath(executablePath))
        {
            UseShellExecute = false,
            WorkingDirectory = Path.GetFullPath(workingDirectory)
        };
        if (!string.IsNullOrWhiteSpace(updateTransactionId))
        {
            if (!Guid.TryParseExact(updateTransactionId, "N", out _))
            {
                throw new ArgumentException("更新事务标识无效", nameof(updateTransactionId));
            }

            startInfo.ArgumentList.Add("--update-transaction");
            startInfo.ArgumentList.Add(updateTransactionId);
        }

        return startInfo;
    }
}
