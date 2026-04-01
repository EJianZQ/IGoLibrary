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
        if (!_appWindowService.AllowClose &&
            DataContext is MainWindowViewModel viewModel &&
            viewModel.ShouldHideToTrayOnClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        if (_notificationService is ToastNotificationService toastNotificationService)
        {
            toastNotificationService.DismissAllImmediately();
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

    private async void OnActivated(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel viewModel)
        {
            await TryAutoParseClipboardAsync(viewModel, isWindowInteractionReady: true);
        }
    }

    private async void OnObservedViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.GrabLogsText))
        {
            Dispatcher.UIThread.Post(() => GrabLogScrollViewer?.ScrollToEnd(), DispatcherPriority.Background);
        }

        if (!IsActive)
        {
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.IsInitializationComplete) &&
            viewModel.IsInitializationComplete)
        {
            await TryAutoParseClipboardAsync(viewModel, isWindowInteractionReady: true);
            return;
        }

        if (e.PropertyName == nameof(MainWindowViewModel.SelectedTabIndex) &&
            viewModel.IsAccountAndVenuePageActive)
        {
            await TryAutoParseClipboardAsync(viewModel, isWindowInteractionReady: true);
        }
    }

    private async void OnVenuePickerItemPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is not Control { DataContext: LibrarySummary library } ||
            DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        await viewModel.HandleVenuePickerLibraryClickAsync(library);
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
            await _notificationService.ShowInfoAsync("已从剪贴板读取", "检测到授权链接，已自动填入并开始解析。");
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
        return string.Equals(clipboardText, lastProcessedClipboardText, StringComparison.Ordinal);
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
