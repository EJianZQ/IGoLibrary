namespace IGoLibrary.Ex.Application.Configuration;

public sealed record NotificationSettings
{
    public TaskEventAlertSettings? TaskEventAlerts { get; init; } = TaskEventAlertSettings.Default;

    public NotificationSettings()
    {
    }

    public NotificationSettings(TaskEventAlertSettings? taskEventAlerts)
    {
        TaskEventAlerts = taskEventAlerts;
    }

    public static NotificationSettings Default { get; } = new();
}
