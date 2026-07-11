namespace IGoLibrary.Ex.Desktop.Startup;

internal sealed class SingleInstanceStartupCoordinator(
    Func<int?, Task> waitForParentExitAsync,
    Func<IDisposable?> tryAcquireLease,
    Action<StartupNotice> showNotice,
    Action<RestartArguments> runPrimaryApplication)
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
            showNotice(StartupNotice.CreateStartupFailure(ex));
            return;
        }

        if (lease is null)
        {
            showNotice(StartupNotice.DuplicateInstance);
            return;
        }

        using (lease)
        {
            runPrimaryApplication(restartArguments);
        }
    }

    private IDisposable? AcquireLeaseAfterParentExit(int? parentProcessId)
    {
        waitForParentExitAsync(parentProcessId).GetAwaiter().GetResult();
        return tryAcquireLease();
    }
}
