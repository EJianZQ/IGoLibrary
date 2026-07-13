using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop;

public partial class MainWindow : Window
{
    private const double GlobalLeakDragThreshold = 6;
    private const double GlobalLeakPriorityAutoScrollEdge = 56;
    private const double GlobalLeakPriorityAutoScrollStep = 24;
    private const double GlobalLeakPriorityDragGhostOffset = 16;
    private static readonly DataFormat<string> GlobalLeakLibraryDragDataFormat =
        DataFormat.CreateStringApplicationFormat("igolibrary-global-leak-library-id");

    private readonly AppWindowService _appWindowService;
    private readonly INotificationService _notificationService;
    private readonly DispatcherTimer _globalLeakPriorityAutoScrollTimer;
    private MainWindowViewModel? _observedViewModel;
    private string? _lastProcessedClipboardText;
    private bool _isAutoParsingClipboard;
    private bool _isClosingAfterFlush;
    private bool _isFlushingBeforeClose;
    private GlobalLeakLibraryPriorityItemViewModel? _globalLeakDragCandidate;
    private Point _globalLeakDragStartPoint;
    private bool _globalLeakDragStarted;
    private int _globalLeakPriorityAutoScrollDirection;

    public MainWindow()
        : this(new AppWindowService(), new NoOpNotificationService())
    {
    }

    public MainWindow(AppWindowService appWindowService, INotificationService notificationService)
    {
        _appWindowService = appWindowService;
        _notificationService = notificationService;
        _globalLeakPriorityAutoScrollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(40)
        };
        _globalLeakPriorityAutoScrollTimer.Tick += OnGlobalLeakPriorityAutoScrollTick;
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
            await viewModel.FlushPendingScheduledStartDefaultsAsync();
        }
        finally
        {
            _isClosingAfterFlush = true;
            _isFlushingBeforeClose = false;
            Close();
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
                "自动读取剪贴板失败");
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
                "自动读取剪贴板失败");
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTabIndex) &&
            (viewModel.IsAccountAndVenuePageActive || viewModel.IsRemoteCheckInPageActive))
        {
            _ = RunUiEventHandlerAsync(
                () => TryAutoParseClipboardAsync(viewModel, isWindowInteractionReady: true),
                _notificationService,
                "自动读取剪贴板失败");
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
            "处理场馆选择失败");
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
                        : "检测到授权链接，已自动填入并开始解析"));
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
        string failureTitle)
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
            var message = string.IsNullOrWhiteSpace(ex.Message)
                ? "界面操作失败，请稍后重试"
                : ex.Message;
            await TryShowNotificationAsync(() => notificationService.ShowWarningAsync(failureTitle, message));
        }
    }

    private static async Task TryShowNotificationAsync(Func<Task> showNotificationAsync)
    {
        try
        {
            await showNotificationAsync();
        }
        catch
        {
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
