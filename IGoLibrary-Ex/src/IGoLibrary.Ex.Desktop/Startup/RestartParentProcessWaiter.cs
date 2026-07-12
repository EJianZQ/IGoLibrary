using System.Diagnostics;

namespace IGoLibrary.Ex.Desktop.Startup;

internal static class RestartParentProcessWaiter
{
    internal static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(60);

    public static async Task WaitForExitAsync(
        int? processId,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!ShouldWait(processId))
        {
            return;
        }

        Process process;
        try
        {
            process = Process.GetProcessById(processId!.Value);
        }
        catch (ArgumentException)
        {
            return;
        }

        using (process)
        using (var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            timeoutSource.CancelAfter(timeout ?? DefaultTimeout);
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException(
                    $"等待旧进程 {processId.Value} 退出超时",
                    ex);
            }
        }
    }

    internal static bool ShouldWait(int? processId)
        => processId is > 0 && processId.Value != Environment.ProcessId;
}
