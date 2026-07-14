namespace IGoLibrary.Ex.Application.Abstractions;

public sealed class ReleaseAssetDownloadPauseController :
    IReleaseAssetDownloadPauseSource,
    IDisposable
{
    private readonly object _gate = new();
    private CancellationTokenSource _pauseRequest = new();
    private TaskCompletionSource? _resumeSignal;
    private bool _isPaused;
    private bool _disposed;

    public bool IsPaused
    {
        get
        {
            lock (_gate)
            {
                return _isPaused;
            }
        }
    }

    public CancellationToken PauseToken
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _pauseRequest.Token;
            }
        }
    }

    public bool TryPause()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_isPaused)
            {
                return false;
            }

            _isPaused = true;
            _resumeSignal = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _pauseRequest.Cancel();
            return true;
        }
    }

    public bool TryResume()
    {
        TaskCompletionSource? resumeSignal;
        CancellationTokenSource previousSource;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (!_isPaused)
            {
                return false;
            }

            _isPaused = false;
            resumeSignal = _resumeSignal;
            _resumeSignal = null;
            previousSource = _pauseRequest;
            _pauseRequest = new CancellationTokenSource();
        }

        resumeSignal?.TrySetResult();
        previousSource.Dispose();
        return true;
    }

    public ValueTask WaitWhilePausedAsync(CancellationToken cancellationToken = default)
    {
        Task? resumeTask;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            resumeTask = _isPaused ? _resumeSignal?.Task : null;
        }

        return resumeTask is null
            ? ValueTask.CompletedTask
            : new ValueTask(resumeTask.WaitAsync(cancellationToken));
    }

    public void Dispose()
    {
        CancellationTokenSource source;
        TaskCompletionSource? resumeSignal;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            source = _pauseRequest;
            resumeSignal = _resumeSignal;
            _resumeSignal = null;
        }

        source.Cancel();
        resumeSignal?.TrySetCanceled();
        source.Dispose();
    }
}
