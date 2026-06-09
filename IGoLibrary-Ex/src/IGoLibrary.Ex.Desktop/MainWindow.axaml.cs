using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
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
    private readonly AppWindowService _appWindowService;
    private readonly INotificationService _notificationService;
    private MainWindowViewModel? _observedViewModel;
    private string? _lastProcessedClipboardText;
    private bool _isAutoParsingClipboard;
    private bool _isClosingAfterFlush;
    private bool _isFlushingBeforeClose;

    public MainWindow()
        : this(new AppWindowService(), new NoOpNotificationService())
    {
    }

    public MainWindow(AppWindowService appWindowService, INotificationService notificationService)
    {
        _appWindowService = appWindowService;
        _notificationService = notificationService;
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
            viewModel.IsAccountAndVenuePageActive)
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
            await TryShowNotificationAsync(
                () => _notificationService.ShowInfoAsync("已从剪贴板读取", "检测到授权链接，已自动填入并开始解析"));
            await viewModel.TryAutoParseClipboardLinkAsync(clipboardText);
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
               !viewModel.IsAuthorized &&
               viewModel.IsAccountAndVenuePageActive;
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
