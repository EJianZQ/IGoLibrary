using IGoLibrary.Ex.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Application.Services;

public sealed class LoggingSettingsWorkflowService(
    ISettingsService settingsService,
    IAppLogRuntimeController runtimeController,
    ILogger<LoggingSettingsWorkflowService>? logger = null) : ILoggingSettingsWorkflowService
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<LoggingSettingsUpdateResult> SaveAsync(
        LogFileSettings settings, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try { return await SaveCoreAsync(settings, cancellationToken); }
        finally { _gate.Release(); }
    }

    private async Task<LoggingSettingsUpdateResult> SaveCoreAsync(
        LogFileSettings settings,
        CancellationToken cancellationToken = default)
    {
        var normalized = LogFileSettings.Normalize(settings);
        var saved = await settingsService.UpdateAsync(current =>
        {
            if (current.Logging == normalized)
            {
                return current;
            }

            return current with { Logging = normalized };
        }, cancellationToken);

        var effective = LogFileSettings.Normalize(saved.Logging);
        // Persistence has already committed; runtime application must finish even if the caller
        // cancels at this point so the process cannot drift from the stored value.
        LogRuntimeApplyResult runtimeResult;
        try { runtimeResult = await runtimeController.ApplyAsync(effective, CancellationToken.None); }
        catch (Exception ex)
        {
            runtimeResult = LogRuntimeApplyResult.Success with
            {
                ApplicationFailure = $"应用日志设置失败：{ex.GetType().Name}"
            };
        }
        try
        {
            if (runtimeResult.ApplicationFailure is null)
            {
                logger?.LogInformation(
                    "日志设置已保存并应用。文件日志={FileLoggingEnabled}；网络请求记录={RecordNetworkRequests}；保留文件数量={RetainedFileCount}。",
                    effective.Enabled, effective.RecordNetworkRequests, effective.RetainedFileCount);
            }
            else
            {
                logger?.LogWarning("日志设置已保存，但运行时应用失败。原因={ApplicationFailure}。", runtimeResult.ApplicationFailure);
            }
        }
        catch
        {
            // Diagnostics cannot turn a committed settings update into a reported save failure.
        }
        return new LoggingSettingsUpdateResult(effective, runtimeResult);
    }
}
