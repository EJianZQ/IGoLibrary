using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class LogLineViewModel : ObservableObject
{
    private static readonly IBrush DefaultBrush = new SolidColorBrush(Color.Parse("#1D2129"));
    private static readonly IBrush SuccessBrush = new SolidColorBrush(Color.Parse("#16A34A"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#DC2626"));

    public LogLineViewModel(
        string timestampText,
        string message,
        LogEntryKind kind,
        bool isLatest,
        bool hasSuccessSemantic,
        bool hasFailureSemantic)
    {
        TimestampText = timestampText;
        Message = message;
        Kind = kind;
        this.isLatest = isLatest;
        HasSuccessSemantic = hasSuccessSemantic;
        HasFailureSemantic = hasFailureSemantic;
    }

    public string TimestampText { get; }

    public string Message { get; }

    public LogEntryKind Kind { get; }

    public bool HasSuccessSemantic { get; }

    public bool HasFailureSemantic { get; }

    [ObservableProperty]
    private bool isLatest;

    public IBrush MessageBrush => HasSuccessSemantic
        ? SuccessBrush
        : HasFailureSemantic
            ? ErrorBrush
            : DefaultBrush;

    partial void OnIsLatestChanged(bool value)
    {
        OnPropertyChanged(nameof(MessageBrush));
    }
}
