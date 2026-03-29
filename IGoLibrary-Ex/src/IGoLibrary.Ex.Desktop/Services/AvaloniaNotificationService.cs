using System.Collections.Concurrent;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class AvaloniaNotificationService : INotificationService
{
    private readonly ConcurrentQueue<Notification> _pending = new();
    private readonly ISettingsService? _settingsService;
    private WindowNotificationManager? _manager;

    public AvaloniaNotificationService()
    {
    }

    public AvaloniaNotificationService(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void Attach(Window window)
    {
        _manager = new WindowNotificationManager(window)
        {
            Position = NotificationPosition.TopRight,
            MaxItems = 5
        };

        while (_pending.TryDequeue(out var pending))
        {
            _manager.Show(pending);
        }
    }

    public async Task ShowInfoAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync(cancellationToken))
        {
            return;
        }

        await ShowAsync(NotificationType.Information, title, message);
    }

    public async Task ShowWarningAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync(cancellationToken))
        {
            return;
        }

        await ShowAsync(NotificationType.Warning, title, message);
    }

    public async Task ShowSuccessAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        if (!await IsEnabledAsync(cancellationToken))
        {
            return;
        }

        await ShowAsync(NotificationType.Success, title, message);
    }

    private Task ShowAsync(NotificationType type, string title, string message)
    {
        var notification = new Notification(title, message, type);
        Dispatcher.UIThread.Post(() =>
        {
            if (_manager is null)
            {
                _pending.Enqueue(notification);
            }
            else
            {
                _manager.Show(notification);
            }
        });

        return Task.CompletedTask;
    }

    private async Task<bool> IsEnabledAsync(CancellationToken cancellationToken)
    {
        if (_settingsService is null)
        {
            return true;
        }

        var settings = await _settingsService.LoadAsync(cancellationToken);
        return settings.NotificationsEnabled;
    }
}
