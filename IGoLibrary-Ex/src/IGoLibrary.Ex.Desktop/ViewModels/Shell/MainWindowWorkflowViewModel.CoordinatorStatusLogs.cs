using System.Collections.ObjectModel;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    private bool _activityLogsConfigured;

    public ObservableCollection<LogLineViewModel> OccupyLogLines => ActivityLogs.OccupyLogLines;

    public string AllLogsText
    {
        get => ActivityLogs.AllLogsText;
        set => ActivityLogs.AllLogsText = value;
    }

    public string GrabLogsText
    {
        get => ActivityLogs.GrabLogsText;
        set => ActivityLogs.GrabLogsText = value;
    }

    public string OccupyLogsText
    {
        get => ActivityLogs.OccupyLogsText;
        set => ActivityLogs.OccupyLogsText = value;
    }

    public string TomorrowLogsText
    {
        get => ActivityLogs.TomorrowLogsText;
        set => ActivityLogs.TomorrowLogsText = value;
    }

    public string GlobalLeakLogsText
    {
        get => ActivityLogs.GlobalLeakLogsText;
        set => ActivityLogs.GlobalLeakLogsText = value;
    }

    private void ConfigureActivityLogsPropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        propertyBridge.ForwardSame(
            ActivityLogs,
            nameof(AllLogsText),
            nameof(GrabLogsText),
            nameof(OccupyLogsText),
            nameof(TomorrowLogsText),
            nameof(GlobalLeakLogsText));
    }

    private void EnsureActivityLogsConfigured()
    {
        if (_activityLogsConfigured)
        {
            return;
        }

        EnsureOccupyPageConfigured();
        ActivityLogs.Configure(
            OccupyPage.TryRecordOccupySuccess,
            () => RefreshReservationAsync(showNotificationOnError: false));
        _activityLogsConfigured = true;
    }

    private void OnLogEntryWritten(object? sender, AppLogEntry entry)
    {
        EnsureActivityLogsConfigured();
        ActivityLogs.OnLogEntryWritten(sender, entry);
    }
}
