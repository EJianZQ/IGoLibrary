namespace IGoLibrary.Ex.Domain.Models;

public sealed record AppSettings
{
    public NotificationSettings Notifications { get; init; } = NotificationSettings.Default;

    public AppUiSettings Ui { get; init; } = AppUiSettings.Default;

    public ProtocolSettings Protocol { get; init; } = ProtocolSettings.Default;

    public RequestPolicySettings RequestPolicy { get; init; } = RequestPolicySettings.Default;

    public TaskExecutionSettings Tasks { get; init; } = TaskExecutionSettings.Default;

    public VenueSelectionSettings Venue { get; init; } = VenueSelectionSettings.Default;

    public DashboardMetrics Dashboard { get; init; } = DashboardMetrics.Default;

    public static AppSettings Default { get; } = new();
}
