using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using IGoLibrary.Ex.Desktop.Services;
using Avalonia.Threading;
using System.Diagnostics;

namespace IGoLibrary.Ex.Desktop;

public partial class ToastWindow : Window
{
    private static readonly TimeSpan ShowAnimationDuration = TimeSpan.FromMilliseconds(300);
    private static readonly TimeSpan MoveAnimationDuration = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan CloseAnimationDuration = TimeSpan.FromMilliseconds(180);
    private const int EnterOvershootX = 24;
    private const int ExitOffsetY = 14;
    private static readonly IBrush InfoAccentBrush = new SolidColorBrush(Color.Parse("#0F6FFF"));
    private static readonly IBrush InfoSoftBrush = new SolidColorBrush(Color.Parse("#EAF2FF"));
    private static readonly IBrush WarningAccentBrush = new SolidColorBrush(Color.Parse("#C27803"));
    private static readonly IBrush WarningSoftBrush = new SolidColorBrush(Color.Parse("#FFF4D6"));
    private static readonly IBrush SuccessAccentBrush = new SolidColorBrush(Color.Parse("#14804A"));
    private static readonly IBrush SuccessSoftBrush = new SolidColorBrush(Color.Parse("#E6F8EE"));
    private static readonly IBrush SurfaceBrushValue = new SolidColorBrush(Color.Parse("#FDFDFE"));
    private static readonly IBrush BorderBrushValue = new SolidColorBrush(Color.Parse("#D8E0EA"));
    private static readonly IBrush TitleBrushValue = new SolidColorBrush(Color.Parse("#111827"));
    private static readonly IBrush MessageBrushValue = new SolidColorBrush(Color.Parse("#516072"));
    private static readonly IBrush CloseBrushValue = new SolidColorBrush(Color.Parse("#7A8699"));
    private CancellationTokenSource? _movementAnimationCts;
    private CancellationTokenSource? _lifecycleAnimationCts;
    private PixelPoint _anchoredPosition;
    private bool _isOpening;
    private bool _isClosing;

    public ToastWindow()
        : this(ToastVisualKind.Info, string.Empty, string.Empty)
    {
    }

    public ToastWindow(ToastVisualKind kind, string title, string message)
    {
        Kind = kind;
        TitleText = title;
        MessageText = message;
        _anchoredPosition = new PixelPoint(-32000, -32000);
        Position = _anchoredPosition;
        Opacity = 0;
        InitializeComponent();
        DataContext = this;
    }

    public event EventHandler? CloseRequested;

    public ToastVisualKind Kind { get; }

    public string TitleText { get; }

    public string MessageText { get; }

    public bool HasShownAnimation { get; private set; }

    public string KindText => Kind switch
    {
        ToastVisualKind.Warning => "警告",
        ToastVisualKind.Success => "成功",
        _ => "提示"
    };

    public string BadgeText => Kind switch
    {
        ToastVisualKind.Warning => "!",
        ToastVisualKind.Success => "OK",
        _ => "i"
    };

    public IBrush AccentBrush => Kind switch
    {
        ToastVisualKind.Warning => WarningAccentBrush,
        ToastVisualKind.Success => SuccessAccentBrush,
        _ => InfoAccentBrush
    };

    public IBrush AccentSoftBrush => Kind switch
    {
        ToastVisualKind.Warning => WarningSoftBrush,
        ToastVisualKind.Success => SuccessSoftBrush,
        _ => InfoSoftBrush
    };

    public IBrush SurfaceBrush => SurfaceBrushValue;

    public IBrush ToastBorderBrush => BorderBrushValue;

    public IBrush TitleBrush => TitleBrushValue;

    public IBrush MessageBrush => MessageBrushValue;

    public IBrush CloseBrush => CloseBrushValue;

    private void OnCloseButtonClick(object? sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    public void SetAnchoredPosition(PixelPoint targetPosition, bool animate)
    {
        _anchoredPosition = targetPosition;

        if (_isClosing)
        {
            return;
        }

        if (!animate || !HasShownAnimation || _isOpening || !IsVisible)
        {
            Position = targetPosition;
            return;
        }

        StartMovementAnimation(targetPosition);
    }

    public void BeginShowAnimation(int? screenRight = null)
    {
        if (HasShownAnimation || _isOpening || _isClosing)
        {
            return;
        }

        HasShownAnimation = true;
        _isOpening = true;
        CancelLifecycleAnimation();

        var targetPosition = _anchoredPosition;
        var startPosition = new PixelPoint(
            screenRight is int right ? right + EnterOvershootX : targetPosition.X + 56,
            targetPosition.Y);
        Position = startPosition;
        Opacity = 0;

        var cts = new CancellationTokenSource();
        _lifecycleAnimationCts = cts;
        _ = AnimateWindowAsync(
            startPosition,
            targetPosition,
            0,
            1,
            ShowAnimationDuration,
            EaseOutCubic,
            cts.Token,
            () =>
            {
                _isOpening = false;
                Position = _anchoredPosition;
                Opacity = 1;
                if (ReferenceEquals(_lifecycleAnimationCts, cts))
                {
                    _lifecycleAnimationCts.Dispose();
                    _lifecycleAnimationCts = null;
                }
            });
    }

    public bool BeginCloseAnimation()
    {
        if (_isClosing)
        {
            return false;
        }

        _isClosing = true;
        _isOpening = false;
        CancelMovementAnimation();
        CancelLifecycleAnimation();

        var startPosition = Position;
        var targetPosition = new PixelPoint(startPosition.X, startPosition.Y + ExitOffsetY);
        var startOpacity = Opacity;

        var cts = new CancellationTokenSource();
        _lifecycleAnimationCts = cts;
        _ = AnimateWindowAsync(
            startPosition,
            targetPosition,
            startOpacity,
            0,
            CloseAnimationDuration,
            EaseInCubic,
            cts.Token,
            () =>
            {
                Opacity = 0;
                if (ReferenceEquals(_lifecycleAnimationCts, cts))
                {
                    _lifecycleAnimationCts.Dispose();
                    _lifecycleAnimationCts = null;
                }

                Close();
            });

        return true;
    }

    public void CloseImmediately()
    {
        _isClosing = true;
        _isOpening = false;
        CancelMovementAnimation();
        CancelLifecycleAnimation();
        Close();
    }

    private void StartMovementAnimation(PixelPoint targetPosition)
    {
        CancelMovementAnimation();

        var startPosition = Position;
        if (startPosition == targetPosition)
        {
            return;
        }

        var cts = new CancellationTokenSource();
        _movementAnimationCts = cts;
        _ = AnimatePositionAsync(startPosition, targetPosition, MoveAnimationDuration, EaseOutCubic, cts);
    }

    private async Task AnimatePositionAsync(
        PixelPoint startPosition,
        PixelPoint targetPosition,
        TimeSpan duration,
        Func<double, double> easing,
        CancellationTokenSource animationCts)
    {
        try
        {
            await RunAnimationLoopAsync(
                duration,
                easing,
                animationCts.Token,
                progress =>
                {
                    Position = Lerp(startPosition, targetPosition, progress);
                },
                () =>
                {
                    Position = _anchoredPosition;
                    if (ReferenceEquals(_movementAnimationCts, animationCts))
                    {
                        _movementAnimationCts.Dispose();
                        _movementAnimationCts = null;
                    }
                });
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task AnimateWindowAsync(
        PixelPoint startPosition,
        PixelPoint targetPosition,
        double startOpacity,
        double targetOpacity,
        TimeSpan duration,
        Func<double, double> easing,
        CancellationToken cancellationToken,
        Action onCompleted)
    {
        try
        {
            await RunAnimationLoopAsync(
                duration,
                easing,
                cancellationToken,
                progress =>
                {
                    Position = Lerp(startPosition, targetPosition, progress);
                    Opacity = Lerp(startOpacity, targetOpacity, progress);
                },
                onCompleted);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static async Task RunAnimationLoopAsync(
        TimeSpan duration,
        Func<double, double> easing,
        CancellationToken cancellationToken,
        Action<double> applyFrame,
        Action onCompleted)
    {
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var rawProgress = duration <= TimeSpan.Zero
                ? 1
                : Math.Clamp(stopwatch.Elapsed.TotalMilliseconds / duration.TotalMilliseconds, 0, 1);
            var easedProgress = easing(rawProgress);

            await Dispatcher.UIThread.InvokeAsync(() => applyFrame(easedProgress));
            if (rawProgress >= 1)
            {
                await Dispatcher.UIThread.InvokeAsync(onCompleted);
                return;
            }

            await Task.Delay(16, cancellationToken);
        }
    }

    private void CancelMovementAnimation()
    {
        if (_movementAnimationCts is null)
        {
            return;
        }

        _movementAnimationCts.Cancel();
        _movementAnimationCts.Dispose();
        _movementAnimationCts = null;
    }

    private void CancelLifecycleAnimation()
    {
        if (_lifecycleAnimationCts is null)
        {
            return;
        }

        _lifecycleAnimationCts.Cancel();
        _lifecycleAnimationCts.Dispose();
        _lifecycleAnimationCts = null;
    }

    private static PixelPoint Lerp(PixelPoint from, PixelPoint to, double progress)
    {
        var x = (int)Math.Round(from.X + ((to.X - from.X) * progress));
        var y = (int)Math.Round(from.Y + ((to.Y - from.Y) * progress));
        return new PixelPoint(x, y);
    }

    private static double Lerp(double from, double to, double progress)
    {
        return from + ((to - from) * progress);
    }

    private static double EaseOutCubic(double progress)
    {
        return 1 - Math.Pow(1 - progress, 3);
    }

    private static double EaseInCubic(double progress)
    {
        return progress * progress * progress;
    }
}
