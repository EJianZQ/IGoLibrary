namespace IGoLibrary.Ex.Application.Services;

/// <summary>只串行化黑名单保存和启动接收，不锁定网络请求或任务生命周期。</summary>
public sealed class GlobalLeakConfigurationGate : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task EnterAsync(CancellationToken cancellationToken = default) => _gate.WaitAsync(cancellationToken);

    public void Exit() => _gate.Release();

    public void Dispose() => _gate.Dispose();
}
