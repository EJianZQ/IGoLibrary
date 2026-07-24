using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class DataRestoreRestartService(
    AppWindowService appWindowService,
    ILogger<DataRestoreRestartService>? logger = null) : IDataRestoreRestartService
{
    private readonly ILogger<DataRestoreRestartService> _logger =
        logger ?? NullLogger<DataRestoreRestartService>.Instance;

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
        _logger.LogInformation(
            "数据恢复重启子进程已启动。事务标识={TransactionId}，子进程标识={ChildProcessId}，父进程标识={ParentProcessId}。",
            transactionId,
            process.Id,
            Environment.ProcessId);
        appWindowService.QuitApplication();
        return Task.CompletedTask;
    }
}
