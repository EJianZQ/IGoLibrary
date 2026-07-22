using System.Diagnostics;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class ApplicationRestartService(AppWindowService appWindowService) : IApplicationRestartService
{
    public Task RestartAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var processPath = Environment.ProcessPath
                          ?? throw new InvalidOperationException("无法确定当前可执行文件路径");
        var startInfo = BuildStartInfo(
            processPath,
            Environment.GetCommandLineArgs(),
            Environment.ProcessId);
        var process = Process.Start(startInfo)
                      ?? throw new InvalidOperationException("无法启动新的应用进程");
        process.Dispose();
        appWindowService.QuitApplication();
        return Task.CompletedTask;
    }

    internal static ProcessStartInfo BuildStartInfo(
        string processPath,
        IReadOnlyList<string> commandLineArguments,
        int parentProcessId)
    {
        var info = new ProcessStartInfo(processPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var startIndex = commandLineArguments.Count > 0 &&
                         PathsEqual(commandLineArguments[0], processPath)
            ? 1
            : 0;
        for (var index = startIndex; index < commandLineArguments.Count; index++)
        {
            if (string.Equals(
                    commandLineArguments[index],
                    RestartArguments.ParentProcessIdOption,
                    StringComparison.Ordinal) ||
                string.Equals(
                    commandLineArguments[index],
                    RestartArguments.RestoreTransactionOption,
                    StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            info.ArgumentList.Add(commandLineArguments[index]);
        }

        info.ArgumentList.Add(RestartArguments.ParentProcessIdOption);
        info.ArgumentList.Add(parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return info;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left),
                Path.GetFullPath(right),
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }
}
