using Avalonia;
using Avalonia.Controls;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class MainWindowSizePersistenceService : IMainWindowSizePersistenceService
{
    internal static readonly TimeSpan DefaultDebounceDelay = TimeSpan.FromMilliseconds(500);

    private readonly ISettingsWorkflowService _settingsWorkflowService;
    private readonly IActivityLogService _activityLogService;
    private readonly DeferredAutoSaveController _autoSave;
    private Window? _window;
    private Size? _lastNormalClientSize;
    private Size? _pendingClientSize;
    private long _pendingVersion;
    private bool _hasDirtySize;
    private bool _rememberSizeEnabled;

    public MainWindowSizePersistenceService(
        ISettingsWorkflowService settingsWorkflowService,
        IActivityLogService activityLogService)
        : this(settingsWorkflowService, activityLogService, DefaultDebounceDelay)
    {
    }

    internal MainWindowSizePersistenceService(
        ISettingsWorkflowService settingsWorkflowService,
        IActivityLogService activityLogService,
        TimeSpan debounceDelay)
    {
        _settingsWorkflowService = settingsWorkflowService;
        _activityLogService = activityLogService;
        _autoSave = new DeferredAutoSaveController(debounceDelay, PersistPendingSizeAsync);
    }

    public async Task InitializeAsync(Window window, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(window);
        DetachWindow();
        _window = window;

        try
        {
            var settings = await _settingsWorkflowService.LoadAsync(cancellationToken);
            var preferences = MainViewSizePreferences.Normalize(settings.Ui.MainViewSize);
            _rememberSizeEnabled = preferences.RememberSize;
            if (TryGetSafeRestoredSize(window, preferences, out var restoredSize))
            {
                window.Width = restoredSize.Width;
                window.Height = restoredSize.Height;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _rememberSizeEnabled = false;
            _activityLogService.Write(
                LogEntryKind.Warning,
                "Window",
                $"恢复主窗口大小失败：{ex.Message}");
        }

        if (window.WindowState == WindowState.Normal && TryNormalizeSize(window.ClientSize, out var currentSize))
        {
            _lastNormalClientSize = currentSize;
        }

        window.Resized += OnWindowResized;
        window.Closed += OnWindowClosed;
    }

    public void SetRememberSizeEnabled(bool enabled, bool captureCurrentSize)
    {
        _rememberSizeEnabled = enabled;
        if (!enabled)
        {
            CancelPendingSize();
            return;
        }

        if (!captureCurrentSize)
        {
            return;
        }

        var candidate = GetCurrentNormalClientSize() ?? _lastNormalClientSize;
        if (candidate is { } clientSize)
        {
            QueueSizeSave(clientSize);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        _autoSave.Cancel();
        if (!_rememberSizeEnabled || !_hasDirtySize || _pendingClientSize is null)
        {
            return;
        }

        try
        {
            await PersistPendingSizeAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LogSaveFailure(ex);
        }
    }

    internal void ProcessResize(
        Size clientSize,
        WindowResizeReason reason,
        WindowState windowState)
    {
        if (windowState != WindowState.Normal ||
            !TryNormalizeSize(clientSize, out var normalizedSize))
        {
            return;
        }

        _lastNormalClientSize = normalizedSize;
        if (reason == WindowResizeReason.User && _rememberSizeEnabled)
        {
            QueueSizeSave(normalizedSize);
        }
    }

    internal static bool TryGetSafeRestoredSize(
        MainViewSizePreferences preferences,
        double minWidth,
        double minHeight,
        Size workingArea,
        out Size restoredSize)
    {
        restoredSize = default;
        if (!preferences.RememberSize ||
            !MainViewSizePreferences.TryNormalizeSize(
                preferences.ClientWidth,
                preferences.ClientHeight,
                out var clientWidth,
                out var clientHeight) ||
            !TryNormalizeSize(workingArea, out var normalizedWorkingArea))
        {
            return false;
        }

        var normalizedMinWidth = double.IsFinite(minWidth) && minWidth > 0 ? minWidth : 1;
        var normalizedMinHeight = double.IsFinite(minHeight) && minHeight > 0 ? minHeight : 1;
        var maxWidth = Math.Max(normalizedMinWidth, normalizedWorkingArea.Width);
        var maxHeight = Math.Max(normalizedMinHeight, normalizedWorkingArea.Height);
        restoredSize = new Size(
            Math.Clamp(clientWidth, normalizedMinWidth, maxWidth),
            Math.Clamp(clientHeight, normalizedMinHeight, maxHeight));
        return true;
    }

    private static bool TryGetSafeRestoredSize(
        Window window,
        MainViewSizePreferences preferences,
        out Size restoredSize)
    {
        restoredSize = default;
        var screen = window.Screens.Primary;
        if (screen is null || !double.IsFinite(screen.Scaling) || screen.Scaling <= 0)
        {
            return false;
        }

        var workingArea = new Size(
            screen.WorkingArea.Width / screen.Scaling,
            screen.WorkingArea.Height / screen.Scaling);
        return TryGetSafeRestoredSize(
            preferences,
            window.MinWidth,
            window.MinHeight,
            workingArea,
            out restoredSize);
    }

    private void OnWindowResized(object? sender, WindowResizedEventArgs e)
    {
        if (sender is Window window)
        {
            ProcessResize(e.ClientSize, e.Reason, window.WindowState);
        }
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        DetachWindow();
        _autoSave.Cancel();
    }

    private Size? GetCurrentNormalClientSize()
    {
        return _window is { WindowState: WindowState.Normal } window &&
               TryNormalizeSize(window.ClientSize, out var clientSize)
            ? clientSize
            : null;
    }

    private void QueueSizeSave(Size clientSize)
    {
        if (!TryNormalizeSize(clientSize, out var normalizedSize))
        {
            return;
        }

        _pendingClientSize = normalizedSize;
        _pendingVersion++;
        _hasDirtySize = true;
        _autoSave.Schedule(LogSaveFailure);
    }

    private async Task PersistPendingSizeAsync(CancellationToken cancellationToken)
    {
        if (!_rememberSizeEnabled || _pendingClientSize is not { } clientSize)
        {
            return;
        }

        var version = _pendingVersion;
        await _settingsWorkflowService.SaveMainViewSizeAsync(
            clientSize.Width,
            clientSize.Height,
            cancellationToken);
        if (version == _pendingVersion)
        {
            _hasDirtySize = false;
            _pendingClientSize = null;
        }
    }

    private void CancelPendingSize()
    {
        _autoSave.Cancel();
        _pendingClientSize = null;
        _hasDirtySize = false;
        _pendingVersion++;
    }

    private void LogSaveFailure(Exception ex)
    {
        _activityLogService.Write(
            LogEntryKind.Warning,
            "Window",
            $"保存主窗口大小失败：{ex.Message}");
    }

    private void DetachWindow()
    {
        if (_window is null)
        {
            return;
        }

        _window.Resized -= OnWindowResized;
        _window.Closed -= OnWindowClosed;
        _window = null;
    }

    private static bool TryNormalizeSize(Size size, out Size normalizedSize)
    {
        if (!MainViewSizePreferences.TryNormalizeSize(
                size.Width,
                size.Height,
                out var normalizedWidth,
                out var normalizedHeight))
        {
            normalizedSize = default;
            return false;
        }

        normalizedSize = new Size(normalizedWidth, normalizedHeight);
        return true;
    }
}
