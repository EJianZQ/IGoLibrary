namespace IGoLibrary.Ex.Application.Abstractions;

public interface ILoggingSettingsWorkflowService
{
    Task<LoggingSettingsUpdateResult> SaveAsync(
        LogFileSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed record LoggingSettingsUpdateResult(
    LogFileSettings Settings,
    LogRuntimeApplyResult RuntimeResult);
