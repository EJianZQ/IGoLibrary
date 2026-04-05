using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Avalonia.Threading;
using IGoLibrary.Ex.Application.Abstractions;
using System.Diagnostics;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class ToastNotificationService(
    ISettingsService settingsService,
    AppWindowService appWindowService) : INotificationService
{
    private const int MaxVisibleToasts = 5;
    private const int ToastMargin = 20;
    private const int ToastSpacing = 12;
    private static readonly TimeSpan ToastLifetime = TimeSpan.FromSeconds(5.5);

    private readonly object _gate = new();
    private readonly List<ToastWindow> _activeToasts = [];
    private readonly Dictionary<ToastWindow, CancellationTokenSource> _dismissTokens = [];

    public Task ShowInfoAsync(string title, string message, CancellationToken cancellationToken = default)
        => ShowAsync(ToastVisualKind.Info, title, message, cancellationToken);

    public Task ShowWarningAsync(string title, string message, CancellationToken cancellationToken = default)
        => ShowAsync(ToastVisualKind.Warning, title, message, cancellationToken);

    public Task ShowSuccessAsync(string title, string message, CancellationToken cancellationToken = default)
        => ShowAsync(ToastVisualKind.Success, title, message, cancellationToken);

    public Task ShowPreviewAsync(string title, string message, CancellationToken cancellationToken = default)
        => ShowCoreAsync(ToastVisualKind.Info, title, message, skipSettingsCheck: true, cancellationToken);

    public Task ShowForcedAsync(
        ToastVisualKind kind,
        string title,
        string message,
        CancellationToken cancellationToken = default)
        => ShowCoreAsync(kind, title, message, skipSettingsCheck: true, cancellationToken);

    private async Task ShowAsync(
        ToastVisualKind kind,
        string title,
        string message,
        CancellationToken cancellationToken)
    {
        await ShowCoreAsync(kind, title, message, skipSettingsCheck: false, cancellationToken);
    }

    private async Task ShowCoreAsync(
        ToastVisualKind kind,
        string title,
        string message,
        bool skipSettingsCheck,
        CancellationToken cancellationToken)
    {
        if (!skipSettingsCheck && !await IsEnabledAsync(cancellationToken))
        {
            return;
        }

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            ToastWindow? toast = null;
            ToastWindow? overflowToast = null;

            try
            {
                toast = new ToastWindow(kind, title, message);
                toast.Opened += OnToastOpened;
                toast.Closed += OnToastClosed;

                lock (_gate)
                {
                    _activeToasts.Add(toast);
                    _dismissTokens[toast] = new CancellationTokenSource();
                    if (_activeToasts.Count > MaxVisibleToasts)
                    {
                        overflowToast = _activeToasts.FirstOrDefault(existing => existing != toast);
                    }
                }

                toast.Show();
                toast.BeginLifetimeProgress(ToastLifetime);
                ArrangeToasts(preferredToast: toast);
                if (overflowToast is not null)
                {
                    CloseToast(overflowToast, immediate: true);
                }

                _ = AutoDismissAsync(toast);
            }
            catch (Exception ex)
            {
                if (toast is not null)
                {
                    CleanupToastState(toast);
                }

                Debug.WriteLine($"Toast notification failed to show: {ex}");
            }
        });
    }

    public void DismissAllImmediately()
    {
        List<ToastWindow> snapshot;
        lock (_gate)
        {
            snapshot = _activeToasts.ToList();
        }

        foreach (var toast in snapshot)
        {
            lock (_gate)
            {
                if (_dismissTokens.TryGetValue(toast, out var cts))
                {
                    cts.Cancel();
                }
            }

            if (toast.IsVisible)
            {
                toast.CloseImmediately();
            }
            else
            {
                CleanupToastState(toast);
            }
        }
    }

    private async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        return settings.NotificationsEnabled;
    }

    private async Task AutoDismissAsync(ToastWindow toast)
    {
        CancellationToken token;
        lock (_gate)
        {
            if (!_dismissTokens.TryGetValue(toast, out var cts))
            {
                return;
            }

            token = cts.Token;
        }

        try
        {
            await Task.Delay(ToastLifetime, token);
            await Dispatcher.UIThread.InvokeAsync(() => CloseToast(toast));
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnToastOpened(object? sender, EventArgs e)
    {
        if (sender is ToastWindow toast)
        {
            ArrangeToasts(toast);
            var screen = ResolveTargetScreen([toast], toast);
            toast.BeginShowAnimation(screen?.WorkingArea.Right);
        }
    }

    private void OnToastClosed(object? sender, EventArgs e)
    {
        if (sender is not ToastWindow toast)
        {
            return;
        }

        CleanupToastState(toast);
        ArrangeToasts();
    }

    private void CloseToast(ToastWindow toast, bool immediate = false)
    {
        lock (_gate)
        {
            if (_dismissTokens.TryGetValue(toast, out var cts))
            {
                cts.Cancel();
            }
        }

        if (!toast.IsVisible)
        {
            return;
        }

        if (immediate)
        {
            toast.CloseImmediately();
            return;
        }

        _ = toast.BeginCloseAnimation();
    }

    private void ArrangeToasts(ToastWindow? preferredToast = null)
    {
        IReadOnlyList<ToastWindow> snapshot;
        lock (_gate)
        {
            snapshot = _activeToasts
                .Where(toast => toast.IsVisible)
                .ToList();
        }

        if (snapshot.Count == 0)
        {
            return;
        }

        var screen = ResolveTargetScreen(snapshot, preferredToast);
        if (screen is null)
        {
            return;
        }

        var sizes = new List<PixelSize>(snapshot.Count);
        var positionableToasts = new List<ToastWindow>(snapshot.Count);
        foreach (var toast in snapshot)
        {
            var scale = toast.DesktopScaling;
            var width = Math.Max(1, (int)Math.Ceiling(toast.ClientSize.Width * scale));
            var height = Math.Max(1, (int)Math.Ceiling(toast.ClientSize.Height * scale));
            if (width <= 1 || height <= 1)
            {
                continue;
            }

            positionableToasts.Add(toast);
            sizes.Add(new PixelSize(width, height));
        }

        var positions = ToastLayoutCalculator.Calculate(screen.WorkingArea, sizes, ToastMargin, ToastSpacing);
        for (var index = 0; index < positionableToasts.Count; index++)
        {
            var toast = positionableToasts[index];
            var animate = toast.HasShownAnimation;
            toast.SetAnchoredPosition(positions[index], animate);
        }
    }

    private Screen? ResolveTargetScreen(IReadOnlyList<ToastWindow> snapshot, ToastWindow? preferredToast)
    {
        if (preferredToast?.Screens is { } preferredScreens)
        {
            return preferredScreens.ScreenFromWindow(preferredToast) ?? preferredScreens.Primary;
        }

        var mainWindow = appWindowService.MainWindow;
        if (mainWindow?.Screens is { } mainScreens)
        {
            return mainScreens.ScreenFromWindow(mainWindow) ?? mainScreens.Primary;
        }

        if ((Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow is { } lifetimeWindow &&
            lifetimeWindow.Screens is { } lifetimeScreens)
        {
            return lifetimeScreens.ScreenFromWindow(lifetimeWindow) ?? lifetimeScreens.Primary;
        }

        foreach (var toast in snapshot)
        {
            if (toast.Screens is { } toastScreens)
            {
                return toastScreens.ScreenFromWindow(toast) ?? toastScreens.Primary;
            }
        }

        return null;
    }

    private void CleanupToastState(ToastWindow toast)
    {
        toast.Opened -= OnToastOpened;
        toast.Closed -= OnToastClosed;

        lock (_gate)
        {
            _activeToasts.Remove(toast);
            if (_dismissTokens.Remove(toast, out var cts))
            {
                cts.Cancel();
                cts.Dispose();
            }
        }
    }
}
