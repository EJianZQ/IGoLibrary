namespace IGoLibrary.Ex.Desktop.ViewModels;

internal sealed class DeferredAutoSaveController(TimeSpan delay, Func<CancellationToken, Task> saveAsync)
{
    private CancellationTokenSource? _pending;

    public bool HasPending => _pending is not null;

    public void Schedule(Action<Exception>? onFailure = null)
    {
        Cancel();
        var cts = new CancellationTokenSource();
        _pending = cts;
        _ = RunAsync(cts, onFailure);
    }

    public void Cancel()
    {
        if (_pending is null)
        {
            return;
        }

        _pending.Cancel();
        _pending.Dispose();
        _pending = null;
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_pending is null)
        {
            return;
        }

        Cancel();
        await saveAsync(cancellationToken);
    }

    private async Task RunAsync(CancellationTokenSource source, Action<Exception>? onFailure)
    {
        try
        {
            await Task.Delay(delay, source.Token);
            await saveAsync(source.Token);
            ClearCompleted(source);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            onFailure?.Invoke(ex);
            ClearCompleted(source);
        }
    }

    private void ClearCompleted(CancellationTokenSource source)
    {
        if (!ReferenceEquals(_pending, source))
        {
            return;
        }

        _pending.Dispose();
        _pending = null;
    }
}
