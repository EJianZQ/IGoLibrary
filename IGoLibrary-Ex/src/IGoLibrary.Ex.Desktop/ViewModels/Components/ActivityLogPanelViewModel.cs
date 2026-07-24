using System.Collections.ObjectModel;
using System.Text;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class ActivityLogPanelViewModel(
    IActivityLogService activityLogService,
    IAppThemeService appThemeService) : ViewModelBase
{
    private Action<DateTimeOffset> _recordOccupySuccess = static _ => { };
    private Func<Task> _refreshReservationAfterOccupySuccessAsync = static () => Task.CompletedTask;

    public ObservableCollection<LogLineViewModel> OccupyLogLines { get; } = [];

    [ObservableProperty]
    private string allLogsText = string.Empty;

    [ObservableProperty]
    private string grabLogsText = string.Empty;

    [ObservableProperty]
    private string occupyLogsText = string.Empty;

    [ObservableProperty]
    private string tomorrowLogsText = string.Empty;

    [ObservableProperty]
    private string globalLeakLogsText = string.Empty;

    public void Configure(
        Action<DateTimeOffset> recordOccupySuccess,
        Func<Task> refreshReservationAfterOccupySuccessAsync)
    {
        _recordOccupySuccess = recordOccupySuccess;
        _refreshReservationAfterOccupySuccessAsync = refreshReservationAfterOccupySuccessAsync;
    }

    public void OnLogEntryWritten(object? sender, AppLogEntry entry)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var displayMessage = TrimSentenceEnding(entry.Message);
            var line = FormatLogLine(entry, displayMessage);
            AllLogsText = AppendLine(AllLogsText, line);
            if (entry.Category is "Grab" or "Library" or "Favorite" or "Auth")
            {
                GrabLogsText = AppendLine(GrabLogsText, line);
            }

            if (entry.Category is "GlobalLeak" or "Auth")
            {
                GlobalLeakLogsText = AppendLine(GlobalLeakLogsText, line);
            }

            if (entry.Category is "Tomorrow" or "Library" or "Auth")
            {
                TomorrowLogsText = AppendLine(TomorrowLogsText, line);
            }

            if (entry.Category is "Occupy" or "Auth")
            {
                AppendOccupyLog(entry, displayMessage, line);
            }
        });
    }

    private void AppendOccupyLog(AppLogEntry entry, string displayMessage, string line)
    {
        OccupyLogsText = AppendLine(OccupyLogsText, line);
        if (OccupyLogLines.Count > 0)
        {
            OccupyLogLines[^1].IsLatest = false;
        }

        var hasSuccessSemantic = entry.Kind == LogEntryKind.Success ||
                                 entry.Message.Contains("成功", StringComparison.OrdinalIgnoreCase);
        var hasFailureSemantic = entry.Kind == LogEntryKind.Error ||
                                 entry.Message.Contains("失败", StringComparison.OrdinalIgnoreCase);

        OccupyLogLines.Add(new LogLineViewModel(
            $"[{entry.Timestamp:HH:mm:ss}]",
            $"{entry.Category}: {displayMessage}",
            entry.Kind,
            true,
            hasSuccessSemantic,
            hasFailureSemantic,
            appThemeService));

        if (entry.Category == "Occupy" &&
            displayMessage.EndsWith("已重新预约成功", StringComparison.Ordinal))
        {
            _recordOccupySuccess(entry.Timestamp);
        }

        if (entry.Category == "Occupy" && hasSuccessSemantic)
        {
            _ = Dispatcher.UIThread.InvokeAsync(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(350));
                    await _refreshReservationAfterOccupySuccessAsync();
                }
                catch (Exception ex)
                {
                    activityLogService.Write(LogEntryKind.Warning, "Occupy", $"占座成功后刷新预约状态失败：{ex.Message}", ex);
                }
            });
        }
    }

    private static string AppendLine(string current, string line)
    {
        var builder = new StringBuilder(current);
        builder.AppendLine(line);
        return builder.ToString();
    }

    private static string FormatLogLine(AppLogEntry entry, string message)
    {
        return $"[{entry.Timestamp:HH:mm:ss}] {entry.Category}: {message}";
    }

    private static string TrimSentenceEnding(string message)
    {
        return string.IsNullOrEmpty(message)
            ? message
            : message.TrimEnd('。', '.');
    }
}
