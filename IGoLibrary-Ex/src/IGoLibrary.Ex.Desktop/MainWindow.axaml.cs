using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Threading;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop;

public partial class MainWindow : Window
{
    private const double GlobalLeakDragThreshold = 6;
    private const double GlobalLeakPriorityAutoScrollEdge = 56;
    private const double GlobalLeakPriorityAutoScrollStep = 24;
    private const double GlobalLeakPriorityDragGhostOffset = 16;
    private const double ModalAttentionMaximumScale = 0.014;
    private const double ModalAttentionMaximumOffset = 4;
    private static readonly TimeSpan ModalAttentionAnimationDuration = TimeSpan.FromMilliseconds(260);
    private static readonly TimeSpan ModalAttentionAnimationFrameInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan ModalAttentionSoundMinimumInterval = TimeSpan.FromMilliseconds(400);
    private static readonly DataFormat<string> GlobalLeakLibraryDragDataFormat =
        DataFormat.CreateStringApplicationFormat("igolibrary-global-leak-library-id");

    private readonly AppWindowService _appWindowService;
    private readonly INotificationService _notificationService;
    private readonly IAlertSoundService _alertSoundService;
    private readonly IMainWindowSizePersistenceService _windowSizePersistenceService;
    private readonly IAppLogWriter? _logWriter;
    private readonly DispatcherTimer _globalLeakPriorityAutoScrollTimer;
    private readonly DispatcherTimer _modalAttentionAnimationTimer;
    private MainWindowViewModel? _observedViewModel;
    private string? _lastProcessedClipboardText;
    private bool _isAutoParsingClipboard;
    private bool _isClosingAfterFlush;
    private bool _isFlushingBeforeClose;
    private GlobalLeakLibraryPriorityItemViewModel? _globalLeakDragCandidate;
    private Point _globalLeakDragStartPoint;
    private bool _globalLeakDragStarted;
    private int _globalLeakPriorityAutoScrollDirection;
    private Border? _modalAttentionTarget;
    private TransformGroup? _modalAttentionTransform;
    private ScaleTransform? _modalAttentionScaleTransform;
    private TranslateTransform? _modalAttentionTranslateTransform;
    private ITransform? _modalAttentionOriginalTransform;
    private RelativePoint _modalAttentionOriginalTransformOrigin;
    private long _modalAttentionAnimationStartTimestamp;
    private long _lastModalAttentionSoundTimestamp;

    public MainWindow()
        : this(
            new AppWindowService(),
            new NoOpNotificationService(),
            new AlertSoundService(),
            NoOpMainWindowSizePersistenceService.Instance)
    {
    }

    public MainWindow(AppWindowService appWindowService, INotificationService notificationService)
        : this(
            appWindowService,
            notificationService,
            new AlertSoundService(),
            NoOpMainWindowSizePersistenceService.Instance)
    {
    }

    public MainWindow(
        AppWindowService appWindowService,
        INotificationService notificationService,
        IAlertSoundService alertSoundService)
        : this(
            appWindowService,
            notificationService,
            alertSoundService,
            NoOpMainWindowSizePersistenceService.Instance)
    {
    }

    public MainWindow(
        AppWindowService appWindowService,
        INotificationService notificationService,
        IAlertSoundService alertSoundService,
        IMainWindowSizePersistenceService windowSizePersistenceService,
        IAppLogWriter? logWriter = null)
    {
        _appWindowService = appWindowService;
        _notificationService = notificationService;
        _alertSoundService = alertSoundService;
        _windowSizePersistenceService = windowSizePersistenceService;
        _logWriter = logWriter;
        _globalLeakPriorityAutoScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _globalLeakPriorityAutoScrollTimer.Tick += OnGlobalLeakPriorityAutoScrollTick;
        _modalAttentionAnimationTimer = new DispatcherTimer
        {
            Interval = ModalAttentionAnimationFrameInterval
        };
        _modalAttentionAnimationTimer.Tick += OnModalAttentionAnimationTick;
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Opened += OnOpened;
        Activated += OnActivated;
        Closing += OnClosing;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        _appWindowService.Attach(this);
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        StopModalAttentionAnimation();

        if (!_isClosingAfterFlush &&
            !_appWindowService.AllowClose &&
            DataContext is MainWindowViewModel viewModel &&
            viewModel.ShouldHideToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        if (!_isClosingAfterFlush &&
            DataContext is MainWindowViewModel closeViewModel)
        {
            e.Cancel = true;
            if (!_isFlushingBeforeClose)
            {
                _isFlushingBeforeClose = true;
                _ = CloseAfterFlushAsync(closeViewModel);
            }

            return;
        }

        if (_notificationService is ToastNotificationService toastNotificationService)
        {
            toastNotificationService.DismissAllImmediately();
        }
    }

    private async Task CloseAfterFlushAsync(MainWindowViewModel viewModel)
    {
        try
        {
            try
            {
                await viewModel.FlushPendingScheduledStartDefaultsAsync();
            }
            catch (Exception ex)
            {
                _logWriter?.Write(
                    LogLevel.Error,
                    "UI.Shutdown",
                    "关闭窗口前刷新定时任务默认值失败。",
                    ex,
                    new EventId(8101, "ShutdownDefaultsFlushFailed"));
            }
        }
        finally
        {
            try
            {
                try
                {
                    await _windowSizePersistenceService.FlushAsync();
                }
                catch (Exception ex)
                {
                    _logWriter?.Write(
                        LogLevel.Error,
                        "UI.Shutdown",
                        "关闭窗口前保存窗口尺寸失败。",
                        ex,
                        new EventId(8102, "ShutdownWindowSizeFlushFailed"));
                }
            }
            finally
            {
                _isClosingAfterFlush = true;
                _isFlushingBeforeClose = false;
                Close();
            }
        }
    }

    private void OnHyperlinkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.OpenProjectPageCommand.Execute(null);
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        StopModalAttentionAnimation();

        if (_observedViewModel is not null)
        {
            _observedViewModel.OccupyLogLines.CollectionChanged -= OnOccupyLogLinesChanged;
            _observedViewModel.PropertyChanged -= OnObservedViewModelPropertyChanged;
        }

        _observedViewModel = DataContext as MainWindowViewModel;
        if (_observedViewModel is not null)
        {
            _observedViewModel.OccupyLogLines.CollectionChanged += OnOccupyLogLinesChanged;
            _observedViewModel.PropertyChanged += OnObservedViewModelPropertyChanged;
        }
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            _ = RunUiEventHandlerAsync(
                () => TryAutoParseClipboardAsync(viewModel, isWindowInteractionReady: true),
                _notificationService,
                "自动读取剪贴板失败",
                _logWriter);
        }
    }

    private void OnObservedViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.GrabLogsText))
        {
            Dispatcher.UIThread.Post(() => GrabLogScrollViewer?.ScrollToEnd(), DispatcherPriority.Background);
        }

        if (e.PropertyName == nameof(MainWindowViewModel.GlobalLeakLogsText))
        {
            Dispatcher.UIThread.Post(() => GlobalLeakLogScrollViewer?.ScrollToEnd(), DispatcherPriority.Background);
        }

        if (e.PropertyName == nameof(MainWindowViewModel.TomorrowLogsText))
        {
            Dispatcher.UIThread.Post(() => TomorrowLogScrollViewer?.ScrollToEnd(), DispatcherPriority.Background);
        }

        if (e.PropertyName == nameof(MainWindowViewModel.HasOpenModalOverlay) &&
            !viewModel.HasOpenModalOverlay)
        {
            StopModalAttentionAnimation();
        }

        if (!IsActive)
        {
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.IsInitializationComplete) &&
            viewModel.IsInitializationComplete)
        {
            _ = RunUiEventHandlerAsync(
                () => TryAutoParseClipboardAsync(viewModel, isWindowInteractionReady: true),
                _notificationService,
                "自动读取剪贴板失败",
                _logWriter);
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTabIndex) &&
            (viewModel.IsAccountAndVenuePageActive || viewModel.IsRemoteCheckInPageActive))
        {
            _ = RunUiEventHandlerAsync(
                () => TryAutoParseClipboardAsync(viewModel, isWindowInteractionReady: true),
                _notificationService,
                "自动读取剪贴板失败",
                _logWriter);
        }
    }

    private void OnVenuePickerItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control { DataContext: LibrarySummary library } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        _ = RunUiEventHandlerAsync(
            () => viewModel.HandleVenuePickerLibraryClickAsync(library),
            _notificationService,
            "处理场馆选择失败",
            _logWriter);
    }

    private void OnSidebarPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel { HasOpenModalOverlay: true })
        {
            return;
        }

        e.Handled = true;
        NotifyBlockedNavigationAttempt();
    }

    internal bool NotifyBlockedNavigationAttempt()
    {
        if (!TryEmphasizeOpenModal())
        {
            return false;
        }

        PlayModalAttentionSound();
        return true;
    }

    internal bool TryEmphasizeOpenModal()
    {
        var target = FindOpenModal();
        if (target is null)
        {
            return false;
        }

        StopModalAttentionAnimation();

        _modalAttentionTarget = target;
        _modalAttentionOriginalTransform = target.RenderTransform;
        _modalAttentionOriginalTransformOrigin = target.RenderTransformOrigin;
        _modalAttentionScaleTransform = new ScaleTransform(1, 1);
        _modalAttentionTranslateTransform = new TranslateTransform();
        _modalAttentionTransform = new TransformGroup();
        _modalAttentionTransform.Children.Add(_modalAttentionScaleTransform);
        _modalAttentionTransform.Children.Add(_modalAttentionTranslateTransform);
        target.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
        target.RenderTransform = _modalAttentionTransform;
        _modalAttentionAnimationStartTimestamp = Stopwatch.GetTimestamp();
        _modalAttentionAnimationTimer.Start();
        return true;
    }

    private Border? FindOpenModal()
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return null;
        }

        if (viewModel.IsLanCookieRelayDialogOpen)
        {
            return this.FindControl<Border>("LanCookieRelayDialogModal");
        }

        if (viewModel.IsGrabSeatSelectionOverlayOpen)
        {
            return this.FindControl<Border>("GrabSeatSelectionModal");
        }

        if (viewModel.IsMobileControlDetailsOpen)
        {
            return this.FindControl<Border>("MobileControlDetailsModal");
        }

        if (viewModel.IsGlobalLeakLibraryPickerOpen)
        {
            return this.FindControl<Border>("GlobalLeakLibraryPickerModal");
        }

        if (viewModel.IsTomorrowSeatSelectionOverlayOpen)
        {
            return this.FindControl<Border>("TomorrowSeatSelectionModal");
        }

        return viewModel.IsVenuePickerOpen
            ? this.FindControl<Border>("VenuePickerModal")
            : null;
    }

    private void OnModalAttentionAnimationTick(object? sender, EventArgs e)
    {
        if (_modalAttentionTarget is null ||
            _modalAttentionScaleTransform is null ||
            _modalAttentionTranslateTransform is null)
        {
            StopModalAttentionAnimation();
            return;
        }

        var elapsed = Stopwatch.GetElapsedTime(_modalAttentionAnimationStartTimestamp);
        var progress = elapsed.TotalMilliseconds / ModalAttentionAnimationDuration.TotalMilliseconds;
        var scale = CalculateModalAttentionScale(progress);
        _modalAttentionScaleTransform.ScaleX = scale;
        _modalAttentionScaleTransform.ScaleY = scale;
        _modalAttentionTranslateTransform.X = CalculateModalAttentionOffset(progress);

        if (progress >= 1)
        {
            StopModalAttentionAnimation();
        }
    }

    private void StopModalAttentionAnimation()
    {
        _modalAttentionAnimationTimer.Stop();
        if (_modalAttentionTarget is not null &&
            ReferenceEquals(_modalAttentionTarget.RenderTransform, _modalAttentionTransform))
        {
            _modalAttentionTarget.RenderTransform = _modalAttentionOriginalTransform;
            _modalAttentionTarget.RenderTransformOrigin = _modalAttentionOriginalTransformOrigin;
        }

        _modalAttentionTarget = null;
        _modalAttentionTransform = null;
        _modalAttentionScaleTransform = null;
        _modalAttentionTranslateTransform = null;
        _modalAttentionOriginalTransform = null;
    }

    internal static double CalculateModalAttentionScale(double progress)
    {
        var normalized = Math.Clamp(progress, 0, 1);
        return 1 + (ModalAttentionMaximumScale * Math.Sin(Math.PI * normalized));
    }

    internal static double CalculateModalAttentionOffset(double progress)
    {
        var normalized = Math.Clamp(progress, 0, 1);
        return ModalAttentionMaximumOffset *
               Math.Sin(normalized * Math.PI * 4) *
               (1 - normalized);
    }

    private void PlayModalAttentionSound()
    {
        var now = Stopwatch.GetTimestamp();
        if (_lastModalAttentionSoundTimestamp != 0 &&
            Stopwatch.GetElapsedTime(_lastModalAttentionSoundTimestamp, now) < ModalAttentionSoundMinimumInterval)
        {
            return;
        }

        _lastModalAttentionSoundTimestamp = now;
        _ = _alertSoundService.PlaySystemPromptAsync();
    }

    private void OnGrabSeatOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        viewModel.CancelGrabSeatSelectionCommand.Execute(null);
    }

    private static void OnGrabSeatModalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnGlobalLeakLibraryOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        viewModel.CancelGlobalLeakLibrariesCommand.Execute(null);
    }

    private static void OnGlobalLeakLibraryModalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private void OnGlobalLeakPriorityDragHandlePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Control { DataContext: GlobalLeakLibraryPriorityItemViewModel item } control ||
            !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _globalLeakDragCandidate = item;
        _globalLeakDragStartPoint = e.GetPosition(control);
        _globalLeakDragStarted = false;
        e.Pointer.Capture(control);
        e.Handled = true;
    }

    private async void OnGlobalLeakPriorityDragHandlePointerMoved(object? sender, PointerEventArgs e)
    {
        if (sender is not Control control ||
            _globalLeakDragCandidate is null ||
            _globalLeakDragStarted ||
            !e.GetCurrentPoint(control).Properties.IsLeftButtonPressed ||
            !HasExceededGlobalLeakDragThreshold(_globalLeakDragStartPoint, e.GetPosition(control)))
        {
            return;
        }

        _globalLeakDragStarted = true;
        ShowGlobalLeakPriorityDragGhost(_globalLeakDragCandidate, e);
        var dataTransfer = new DataTransfer();
        dataTransfer.Add(DataTransferItem.Create(
            GlobalLeakLibraryDragDataFormat,
            _globalLeakDragCandidate.LibraryId.ToString(CultureInfo.InvariantCulture)));
        e.Pointer.Capture(null);
        e.Handled = true;

        try
        {
            await DragDrop.DoDragDropAsync(e, dataTransfer, DragDropEffects.Move);
        }
        catch (Exception ex)
        {
            _logWriter?.Write(
                LogLevel.Error,
                "UI.DragDrop",
                "调整全域捡漏扫描优先级失败。",
                ex,
                new EventId(8103, "GlobalLeakPriorityDragFailed"));
            await _notificationService.ShowWarningAsync("调整扫描优先级失败", ex.Message);
        }
        finally
        {
            ResetGlobalLeakPriorityDrag();
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.ClearGlobalLeakLibraryDropIndicators();
            }
        }
    }

    private void OnGlobalLeakPriorityDragHandlePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_globalLeakDragStarted)
        {
            ResetGlobalLeakPriorityDrag();
        }
    }

    private void OnGlobalLeakPriorityDragHandlePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_globalLeakDragStarted)
        {
            ResetGlobalLeakPriorityDrag();
        }
    }

    private void OnGlobalLeakPriorityDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not Control { DataContext: GlobalLeakLibraryPriorityItemViewModel target } control ||
            DataContext is not MainWindowViewModel viewModel ||
            !TryGetGlobalLeakDraggedLibraryId(e.DataTransfer, out var sourceLibraryId))
        {
            e.DragEffects = DragDropEffects.None;
            StopGlobalLeakPriorityAutoScroll();
            return;
        }

        UpdateGlobalLeakPriorityAutoScroll(e);
        UpdateGlobalLeakPriorityDragGhost(e);
        if (sourceLibraryId == target.LibraryId)
        {
            viewModel.ClearGlobalLeakLibraryDropIndicators();
            e.DragEffects = DragDropEffects.Move;
            e.Handled = true;
            return;
        }

        var insertAfter = ShouldInsertGlobalLeakPriorityAfter(
            e.GetPosition(control).Y,
            control.Bounds.Height);
        e.DragEffects = viewModel.SetGlobalLeakLibraryDropIndicator(target.LibraryId, insertAfter)
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnGlobalLeakPriorityDragLeave(object? sender, DragEventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ClearGlobalLeakLibraryDropIndicators();
        }
    }

    private void OnGlobalLeakPriorityDrop(object? sender, DragEventArgs e)
    {
        StopGlobalLeakPriorityAutoScroll();
        if (sender is not Control { DataContext: GlobalLeakLibraryPriorityItemViewModel target } control ||
            DataContext is not MainWindowViewModel viewModel ||
            !TryGetGlobalLeakDraggedLibraryId(e.DataTransfer, out var sourceLibraryId))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        var insertAfter = ShouldInsertGlobalLeakPriorityAfter(
            e.GetPosition(control).Y,
            control.Bounds.Height);
        var moved = viewModel.MoveDraftGlobalLeakLibrary(sourceLibraryId, target.LibraryId, insertAfter);
        viewModel.ClearGlobalLeakLibraryDropIndicators();
        e.DragEffects = moved ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnGlobalLeakPriorityScrollViewerDragOver(object? sender, DragEventArgs e)
    {
        if (!TryGetGlobalLeakDraggedLibraryId(e.DataTransfer, out _))
        {
            e.DragEffects = DragDropEffects.None;
            StopGlobalLeakPriorityAutoScroll();
            return;
        }

        UpdateGlobalLeakPriorityAutoScroll(e);
        UpdateGlobalLeakPriorityDragGhost(e);
        e.DragEffects = DragDropEffects.Move;
        e.Handled = true;
    }

    private void OnGlobalLeakPriorityScrollViewerDragLeave(object? sender, DragEventArgs e)
    {
        StopGlobalLeakPriorityAutoScroll();
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ClearGlobalLeakLibraryDropIndicators();
        }
    }

    private void OnGlobalLeakPriorityScrollViewerDrop(object? sender, DragEventArgs e)
    {
        StopGlobalLeakPriorityAutoScroll();
        if (DataContext is MainWindowViewModel viewModel)
        {
            viewModel.ClearGlobalLeakLibraryDropIndicators();
        }

        e.DragEffects = DragDropEffects.None;
        e.Handled = true;
    }

    public static bool HasExceededGlobalLeakDragThreshold(Point origin, Point current)
    {
        return Math.Abs(current.X - origin.X) >= GlobalLeakDragThreshold ||
               Math.Abs(current.Y - origin.Y) >= GlobalLeakDragThreshold;
    }

    public static bool ShouldInsertGlobalLeakPriorityAfter(double pointerY, double targetHeight)
    {
        return targetHeight > 0 && pointerY >= targetHeight / 2;
    }

    public static int GetGlobalLeakPriorityAutoScrollDirection(double pointerY, double viewportHeight)
    {
        if (viewportHeight <= 0)
        {
            return 0;
        }

        var edge = Math.Min(GlobalLeakPriorityAutoScrollEdge, viewportHeight / 3);
        if (pointerY <= edge)
        {
            return -1;
        }

        return pointerY >= viewportHeight - edge ? 1 : 0;
    }

    public static double CalculateGlobalLeakPriorityAutoScrollOffset(
        double currentOffset,
        double maximumOffset,
        int direction)
    {
        return Math.Clamp(
            currentOffset + Math.Sign(direction) * GlobalLeakPriorityAutoScrollStep,
            0,
            Math.Max(0, maximumOffset));
    }

    public static Point CalculateGlobalLeakPriorityDragGhostPosition(
        Point pointerPosition,
        Size containerSize,
        Size ghostSize)
    {
        return new Point(
            Math.Clamp(
                pointerPosition.X + GlobalLeakPriorityDragGhostOffset,
                0,
                Math.Max(0, containerSize.Width - ghostSize.Width)),
            Math.Clamp(
                pointerPosition.Y + GlobalLeakPriorityDragGhostOffset,
                0,
                Math.Max(0, containerSize.Height - ghostSize.Height)));
    }

    private static bool TryGetGlobalLeakDraggedLibraryId(IDataTransfer dataTransfer, out int libraryId)
    {
        var text = dataTransfer.TryGetValue(GlobalLeakLibraryDragDataFormat);
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out libraryId);
    }

    private void ResetGlobalLeakPriorityDrag()
    {
        StopGlobalLeakPriorityAutoScroll();
        HideGlobalLeakPriorityDragGhost();
        _globalLeakDragCandidate = null;
        _globalLeakDragStarted = false;
    }

    private void UpdateGlobalLeakPriorityAutoScroll(DragEventArgs e)
    {
        var scrollViewer = this.FindControl<ScrollViewer>("GlobalLeakPriorityScrollViewer");
        if (scrollViewer is null)
        {
            StopGlobalLeakPriorityAutoScroll();
            return;
        }

        _globalLeakPriorityAutoScrollDirection = GetGlobalLeakPriorityAutoScrollDirection(
            e.GetPosition(scrollViewer).Y,
            scrollViewer.Bounds.Height);
        if (_globalLeakPriorityAutoScrollDirection == 0)
        {
            StopGlobalLeakPriorityAutoScroll();
            return;
        }

        _globalLeakPriorityAutoScrollTimer.Start();
    }

    private void OnGlobalLeakPriorityAutoScrollTick(object? sender, EventArgs e)
    {
        var scrollViewer = this.FindControl<ScrollViewer>("GlobalLeakPriorityScrollViewer");
        if (scrollViewer is null || _globalLeakPriorityAutoScrollDirection == 0)
        {
            StopGlobalLeakPriorityAutoScroll();
            return;
        }

        var maximumOffset = Math.Max(0, scrollViewer.Extent.Height - scrollViewer.Viewport.Height);
        var nextOffset = CalculateGlobalLeakPriorityAutoScrollOffset(
            scrollViewer.Offset.Y,
            maximumOffset,
            _globalLeakPriorityAutoScrollDirection);
        if (Math.Abs(nextOffset - scrollViewer.Offset.Y) < 0.01)
        {
            StopGlobalLeakPriorityAutoScroll();
            return;
        }

        scrollViewer.Offset = new Vector(scrollViewer.Offset.X, nextOffset);
    }

    private void StopGlobalLeakPriorityAutoScroll()
    {
        _globalLeakPriorityAutoScrollTimer.Stop();
        _globalLeakPriorityAutoScrollDirection = 0;
    }

    private void ShowGlobalLeakPriorityDragGhost(
        GlobalLeakLibraryPriorityItemViewModel item,
        PointerEventArgs e)
    {
        var ghost = this.FindControl<Border>("GlobalLeakPriorityDragGhost");
        var overlay = this.FindControl<Canvas>("GlobalLeakPriorityDragOverlay");
        var priorityPanel = this.FindControl<Border>("GlobalLeakPriorityPanel");
        if (ghost is null || overlay is null || priorityPanel is null)
        {
            return;
        }

        ghost.Width = Math.Clamp(priorityPanel.Bounds.Width - 32, 280, 460);
        ghost.DataContext = item;
        ghost.IsVisible = true;
        UpdateGlobalLeakPriorityDragGhostPosition(e.GetPosition(overlay));
    }

    private void UpdateGlobalLeakPriorityDragGhost(DragEventArgs e)
    {
        var ghost = this.FindControl<Border>("GlobalLeakPriorityDragGhost");
        var overlay = this.FindControl<Canvas>("GlobalLeakPriorityDragOverlay");
        if (ghost is not { IsVisible: true } || overlay is null)
        {
            return;
        }

        UpdateGlobalLeakPriorityDragGhostPosition(e.GetPosition(overlay));
    }

    private void UpdateGlobalLeakPriorityDragGhostPosition(Point pointerPosition)
    {
        var ghost = this.FindControl<Border>("GlobalLeakPriorityDragGhost");
        var overlay = this.FindControl<Canvas>("GlobalLeakPriorityDragOverlay");
        if (ghost is null || overlay is null)
        {
            return;
        }

        var ghostSize = new Size(
            double.IsNaN(ghost.Width) ? 340 : ghost.Width,
            ghost.Bounds.Height > 0 ? ghost.Bounds.Height : 64);
        var position = CalculateGlobalLeakPriorityDragGhostPosition(
            pointerPosition,
            overlay.Bounds.Size,
            ghostSize);
        Canvas.SetLeft(ghost, position.X);
        Canvas.SetTop(ghost, position.Y);
    }

    private void HideGlobalLeakPriorityDragGhost()
    {
        if (this.FindControl<Border>("GlobalLeakPriorityDragGhost") is { } ghost)
        {
            ghost.IsVisible = false;
            ghost.DataContext = null;
            Canvas.SetLeft(ghost, 0);
            Canvas.SetTop(ghost, 0);
        }
    }

    private void OnTomorrowSeatOverlayPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        e.Handled = true;
        viewModel.CancelTomorrowSeatSelectionCommand.Execute(null);
    }

    private static void OnTomorrowSeatModalPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        e.Handled = true;
    }

    private async Task TryAutoParseClipboardAsync(MainWindowViewModel viewModel, bool isWindowInteractionReady)
    {
        var clipboard = Clipboard;
        if (!CanTryAutoParseClipboard(viewModel, isWindowInteractionReady, clipboard is not null, _isAutoParsingClipboard) ||
            clipboard is null)
        {
            return;
        }

        try
        {
            var clipboardText = await clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                return;
            }

            clipboardText = clipboardText.Trim();
            if (ShouldSkipClipboardText(clipboardText, _lastProcessedClipboardText))
            {
                return;
            }

            if (!CodeLinkParser.TryExtractCode(clipboardText, out _))
            {
                return;
            }

            _isAutoParsingClipboard = true;
            _lastProcessedClipboardText = clipboardText;
            var isRemoteCheckIn = viewModel.IsRemoteCheckInPageActive;
            await TryShowNotificationAsync(
                () => _notificationService.ShowInfoAsync(
                    "已从剪贴板读取",
                    isRemoteCheckIn
                        ? "检测到新授权链接，正在获取远程签到授权"
                        : "检测到授权链接，已自动填入并开始解析"),
                _logWriter);
            if (isRemoteCheckIn)
            {
                await viewModel.RemoteCheckInPage.TryAutoParseClipboardLinkAsync(clipboardText);
            }
            else
            {
                await viewModel.TryAutoParseClipboardLinkAsync(clipboardText);
            }
        }
        finally
        {
            _isAutoParsingClipboard = false;
        }
    }

    internal static bool CanTryAutoParseClipboard(
        MainWindowViewModel viewModel,
        bool isWindowActive,
        bool clipboardAvailable,
        bool isAutoParsingClipboard)
    {
        return isWindowActive &&
               clipboardAvailable &&
               !isAutoParsingClipboard &&
               viewModel.IsInitializationComplete &&
               ((!viewModel.IsAuthorized && viewModel.IsAccountAndVenuePageActive) ||
                (viewModel.IsAuthorized && viewModel.IsRemoteCheckInPageActive));
    }

    internal static bool ShouldSkipClipboardText(string clipboardText, string? lastProcessedClipboardText)
    {
        if (string.Equals(clipboardText, lastProcessedClipboardText, StringComparison.Ordinal))
        {
            return true;
        }

        return CodeLinkParser.TryExtractCode(clipboardText, out var currentCode) &&
               CodeLinkParser.TryExtractCode(lastProcessedClipboardText, out var lastCode) &&
               string.Equals(currentCode, lastCode, StringComparison.OrdinalIgnoreCase);
    }

    internal static async Task RunUiEventHandlerAsync(
        Func<Task> action,
        INotificationService notificationService,
        string failureTitle,
        IAppLogWriter? logWriter = null)
    {
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logWriter?.Write(
                LogLevel.Error,
                "UI.Event",
                failureTitle,
                ex,
                new EventId(8104, "UiEventHandlerFailed"));
            var message = string.IsNullOrWhiteSpace(ex.Message)
                ? "界面操作失败，请稍后重试"
                : ex.Message;
            await TryShowNotificationAsync(
                () => notificationService.ShowWarningAsync(failureTitle, message),
                logWriter);
        }
    }

    private static async Task TryShowNotificationAsync(
        Func<Task> showNotificationAsync,
        IAppLogWriter? logWriter = null)
    {
        try
        {
            await showNotificationAsync();
        }
        catch (Exception ex)
        {
            logWriter?.Write(
                LogLevel.Warning,
                "UI.Notification",
                "显示界面通知失败。",
                ex,
                new EventId(8105, "UiNotificationFailed"));
        }
    }

    private void OnOccupyLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is null || e.NewItems.Count == 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() => OccupyLogScrollViewer?.ScrollToEnd(), DispatcherPriority.Background);
    }

    private void OnUnsignedIntegerNumericUpDownTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        if (e.Text.Any(ch => !char.IsDigit(ch)))
        {
            e.Handled = true;
        }
    }

    private void OnUnsignedIntegerNumericUpDownKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not InputElement inputElement)
        {
            return;
        }

        TopLevel.GetTopLevel(inputElement)?.FocusManager?.ClearFocus();
        e.Handled = true;
    }
}
