namespace IGoLibrary.Ex.Desktop.Startup;

internal sealed class SingleInstanceStartupCoordinator(
    Func<int?, Task> waitForParentExitAsync,
    Func<IDisposable?> tryAcquireLease,
    Action<StartupNotice> showNotice,
    Action<RestartArguments> runPrimaryApplication,
    Action<Microsoft.Extensions.Logging.LogLevel, string, Exception?>? writeBootstrapLog = null)
{
    public void Run(RestartArguments restartArguments)
    {
        ArgumentNullException.ThrowIfNull(restartArguments);

        IDisposable? lease;
        try
        {
            lease = AcquireLeaseAfterParentExit(restartArguments.ParentProcessId);
        }
        catch (Exception ex)
        {
            writeBootstrapLog?.Invoke(
                Microsoft.Extensions.Logging.LogLevel.Critical,
                "等待旧进程退出或获取单实例锁失败。",
                ex);
            showNotice(StartupNotice.CreateStartupFailure(ex));
            return;
        }

        if (lease is null)
        {
            writeBootstrapLog?.Invoke(
                Microsoft.Extensions.Logging.LogLevel.Information,
                "检测到已有实例运行，本次启动已退出。",
                null);
            showNotice(StartupNotice.DuplicateInstance);
            return;
        }

        using (lease)
        {
            writeBootstrapLog?.Invoke(
                Microsoft.Extensions.Logging.LogLevel.Information,
                "单实例锁获取成功，继续启动主应用。",
                null);
            runPrimaryApplication(restartArguments);
        }
    }

    private IDisposable? AcquireLeaseAfterParentExit(int? parentProcessId)
    {
        waitForParentExitAsync(parentProcessId).GetAwaiter().GetResult();
        return tryAcquireLease();
    }
}
