namespace IGoLibrary.Ex.Application.Configuration;

public sealed record AppSettings
{
    public NotificationSettings Notifications { get; init; } = NotificationSettings.Default;

    public UiPreferences Ui { get; init; } = UiPreferences.Default;

    public TraceIntProtocolSettings TraceIntProtocol { get; init; } = TraceIntProtocolSettings.Default;

    public NetworkRequestSettings Network { get; init; } = NetworkRequestSettings.Default;

    public TaskExecutionSettings Tasks { get; init; } = TaskExecutionSettings.Default;

    public VenueSelectionSettings Venue { get; init; } = VenueSelectionSettings.Default;

    public DashboardMetrics Dashboard { get; init; } = DashboardMetrics.Default;

    public UpdateCheckSettings Updates { get; init; } = UpdateCheckSettings.Default;

    public static AppSettings Default { get; } = new();
}
