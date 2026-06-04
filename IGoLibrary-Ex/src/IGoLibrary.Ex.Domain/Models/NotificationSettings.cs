namespace IGoLibrary.Ex.Domain.Models;

public sealed record NotificationSettings
{
    public bool AppBannerNotificationsEnabled { get; init; } = true;

    public TaskEventAlertSettings? TaskEventAlerts { get; init; } = TaskEventAlertSettings.Default;

    public NotificationSettings()
    {
    }

    public NotificationSettings(bool appBannerNotificationsEnabled, TaskEventAlertSettings? taskEventAlerts)
    {
        AppBannerNotificationsEnabled = appBannerNotificationsEnabled;
        TaskEventAlerts = taskEventAlerts;
    }

    public static NotificationSettings Default { get; } = new();
}
