using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class CoordinatorStatusViewModel(
    string idleStatusText,
    IBrush idleBrush,
    IBrush runningBrush,
    IBrush successBrush,
    IBrush warningBrush,
    IBrush failureBrush,
    Func<DateTimeOffset> getCurrentTime) : ViewModelBase
{
    private CoordinatorTaskState _taskState = CoordinatorTaskState.Idle;
    private CoordinatorStatusReason _statusReason = CoordinatorStatusReason.None;
    private DateTimeOffset? _runtimeStartedAt;

    [ObservableProperty]
    private string statusText = idleStatusText;

    [ObservableProperty]
    private bool isTaskActive;

    [ObservableProperty]
    private int pollCount;

    [ObservableProperty]
    private int requestCount;

    [ObservableProperty]
    private string lastRequestText = "无";

    [ObservableProperty]
    private string runtimeText = "00:00:00";

    public string DashboardStatusText => _taskState switch
    {
        CoordinatorTaskState.Starting => "启动中",
        CoordinatorTaskState.Running => "运行中",
        CoordinatorTaskState.Stopping => "停止中",
        CoordinatorTaskState.Completed when _statusReason == CoordinatorStatusReason.Stopped => "已停止",
        CoordinatorTaskState.Completed => "已完成",
        CoordinatorTaskState.Failed => "异常",
        _ => "未运行"
    };

    public IBrush DashboardStatusBrush => _taskState switch
    {
        CoordinatorTaskState.Starting => warningBrush,
        CoordinatorTaskState.Running => runningBrush,
        CoordinatorTaskState.Stopping => warningBrush,
        CoordinatorTaskState.Completed when _statusReason == CoordinatorStatusReason.Stopped => failureBrush,
        CoordinatorTaskState.Completed => successBrush,
        CoordinatorTaskState.Failed => failureBrush,
        _ => idleBrush
    };

    public void Apply(CoordinatorStatus status, DateTimeOffset? lastRequestAt = null)
    {
        _taskState = status.State;
        _statusReason = status.Reason;
        StatusText = status.Message;
        IsTaskActive = IsActive(status);
        PollCount = status.PollCount;
        RequestCount = status.RequestCount;
        LastRequestText = lastRequestAt is null ? "无" : lastRequestAt.Value.ToString("HH:mm:ss");

        if (IsTaskActive && _runtimeStartedAt is null)
        {
            _runtimeStartedAt = status.StartedAt;
        }
        else if (!IsTaskActive)
        {
            _runtimeStartedAt = null;
        }

        ApplyRuntime();
        OnPropertyChanged(nameof(DashboardStatusText));
        OnPropertyChanged(nameof(DashboardStatusBrush));
    }

    public void ApplyRuntime()
    {
        if (_runtimeStartedAt is null)
        {
            RuntimeText = "00:00:00";
            return;
        }

        RuntimeText = FormatElapsedClock(getCurrentTime() - _runtimeStartedAt.Value);
    }

    private static bool IsActive(CoordinatorStatus status)
    {
        return status.State is CoordinatorTaskState.Starting
            or CoordinatorTaskState.Running
            or CoordinatorTaskState.Stopping;
    }

    private static string FormatElapsedClock(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        return $"{Math.Max(0, (int)elapsed.TotalHours):D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";
    }
}
