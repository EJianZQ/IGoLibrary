using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class LogLineViewModel : ObservableObject
{
    private readonly IAppThemeService _themeService;

    public LogLineViewModel(
        string timestampText,
        string message,
        LogEntryKind kind,
        bool isLatest,
        bool hasSuccessSemantic,
        bool hasFailureSemantic,
        IAppThemeService themeService)
    {
        TimestampText = timestampText;
        Message = message;
        Kind = kind;
        this.isLatest = isLatest;
        HasSuccessSemantic = hasSuccessSemantic;
        HasFailureSemantic = hasFailureSemantic;
        _themeService = themeService;
    }

    public string TimestampText { get; }

    public string Message { get; }

    public LogEntryKind Kind { get; }

    public bool HasSuccessSemantic { get; }

    public bool HasFailureSemantic { get; }

    [ObservableProperty]
    private bool isLatest;

    public IBrush MessageBrush
    {
        get
        {
            var palette = _themeService.CurrentPalette;
            return HasSuccessSemantic
                ? palette.LogSuccessBrush
                : HasFailureSemantic
                    ? palette.LogErrorBrush
                    : palette.LogDefaultBrush;
        }
    }

    public void RefreshTheme()
    {
        OnPropertyChanged(nameof(MessageBrush));
    }

    partial void OnIsLatestChanged(bool value)
    {
        OnPropertyChanged(nameof(MessageBrush));
    }
}
