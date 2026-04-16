using Avalonia.Media;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed record AppThemePalette(
    IBrush IdleBrush,
    IBrush RunningBrush,
    IBrush SuccessBrush,
    IBrush WarningBrush,
    IBrush FailureBrush,
    IBrush RunningSoftBrush,
    IBrush SuccessSoftBrush,
    IBrush WarningSoftBrush,
    IBrush NeutralSoftBrush,
    IBrush NotificationSegmentActiveTextBrush,
    IBrush NotificationSegmentInactiveTextBrush,
    IBrush LogDefaultBrush,
    IBrush LogSuccessBrush,
    IBrush LogErrorBrush);
