namespace IGoLibrary.Ex.Desktop.Services;

public interface IMobileControlStatusSnapshotProvider
{
    Task<MobileControlStatusSnapshot> CreateSnapshotAsync(CancellationToken cancellationToken = default);
}

public interface IMobileControlTaskUiStateAccessor
{
    TimeSpan? TomorrowScheduledStartTime { get; }

    string TomorrowVerificationText { get; }
}
