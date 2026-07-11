namespace IGoLibrary.Ex.Application.Abstractions;

public interface IAppLogRuntimeController
{
    Task<LogRuntimeApplyResult> ApplyAsync(
        LogFileSettings settings,
        CancellationToken cancellationToken = default);
}

public sealed record LogRuntimeApplyResult(
    int LegacyDeleteFailureCount,
    int RetentionDeleteFailureCount)
{
    public static LogRuntimeApplyResult Success { get; } = new(0, 0);

    public int TotalDeleteFailureCount => LegacyDeleteFailureCount + RetentionDeleteFailureCount;
}
