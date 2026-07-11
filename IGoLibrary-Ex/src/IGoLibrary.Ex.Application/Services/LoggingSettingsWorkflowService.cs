using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Application.Services;

public sealed class LoggingSettingsWorkflowService(
    ISettingsService settingsService,
    IAppLogRuntimeController runtimeController) : ILoggingSettingsWorkflowService
{
    public async Task<LoggingSettingsUpdateResult> SaveAsync(
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
        var runtimeResult = await runtimeController.ApplyAsync(effective, CancellationToken.None);
        return new LoggingSettingsUpdateResult(effective, runtimeResult);
    }
}
