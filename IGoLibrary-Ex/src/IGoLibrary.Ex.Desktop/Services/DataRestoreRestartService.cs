using System.Diagnostics;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class DataRestoreRestartService(AppWindowService appWindowService) : IDataRestoreRestartService
{
    public Task RestartAsync(string transactionId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Guid.TryParseExact(transactionId, "N", out _))
        {
            throw new ArgumentException("恢复事务标识无效", nameof(transactionId));
        }

        var processPath = Environment.ProcessPath
                          ?? throw new InvalidOperationException("无法确定当前可执行文件路径");
        var info = ApplicationRestartService.BuildStartInfo(
            processPath,
            Environment.GetCommandLineArgs(),
            Environment.ProcessId);
        info.ArgumentList.Add(RestartArguments.RestoreTransactionOption);
        info.ArgumentList.Add(transactionId);
        using var process = Process.Start(info)
                            ?? throw new InvalidOperationException("无法启动数据恢复进程");
        appWindowService.QuitApplication();
        return Task.CompletedTask;
    }
}
