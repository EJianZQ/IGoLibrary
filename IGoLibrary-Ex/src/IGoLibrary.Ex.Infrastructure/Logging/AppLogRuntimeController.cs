using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Logging;

namespace IGoLibrary.Ex.Infrastructure.Logging;

public sealed class AppLogRuntimeController(
    IAppLogWriter writer,
    NetworkLogState networkState) : IAppLogRuntimeController
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<LogRuntimeApplyResult> ApplyAsync(
        LogFileSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var enabled = settings.Enabled && settings.RecordNetworkRequests;
            if (!enabled) networkState.Apply(false);
            try
            {
                var result = await ((IAppLogRuntimeController)writer).ApplyAsync(settings, CancellationToken.None);
                networkState.Apply(enabled && result.ApplicationFailure is null);
                return result;
            }
            catch (Exception ex)
            {
                networkState.Apply(false);
                return LogRuntimeApplyResult.Success with
                {
                    ApplicationFailure = AppLogSanitizer.Sanitize($"应用日志设置失败：{ex.GetType().Name}")
                };
            }
        }
        finally { _gate.Release(); }
    }
}
