using System.Collections.Specialized;
using System.ComponentModel;
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
    private string? _lastSuccessfullyParsedClipboardText;
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
            await TryAutoParseClipboardAsync(viewModel);
        }
    }

    private async void OnObservedViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.IsInitializationComplete) &&
            sender is MainWindowViewModel viewModel &&
            viewModel.IsInitializationComplete &&
            IsActive)
        {
            await TryAutoParseClipboardAsync(viewModel);
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

    private async Task TryAutoParseClipboardAsync(MainWindowViewModel viewModel)
    {
        if (_isAutoParsingClipboard || Clipboard is null || !viewModel.IsInitializationComplete || viewModel.IsAuthorized)
        {
            return;
        }

        try
        {
            var clipboardText = await Clipboard.TryGetTextAsync();
            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                return;
            }

            clipboardText = clipboardText.Trim();
            if (string.Equals(clipboardText, _lastSuccessfullyParsedClipboardText, StringComparison.Ordinal))
            {
                return;
            }

            if (!CodeLinkParser.TryExtractCode(clipboardText, out _))
            {
                return;
            }

            _isAutoParsingClipboard = true;
            await _notificationService.ShowInfoAsync("已从剪贴板读取", "检测到授权链接，已自动填入并开始解析。");
            var parsed = await viewModel.TryAutoParseClipboardLinkAsync(clipboardText);
            if (parsed)
            {
                _lastSuccessfullyParsedClipboardText = clipboardText;
            }
        }
        finally
        {
            _isAutoParsingClipboard = false;
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
}
