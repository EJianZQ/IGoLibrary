using System.Net;
using Avalonia.Controls;
using Avalonia.Media;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Notifications;
using MailKit.Security;
using MimeKit;

namespace IGoLibrary.Ex.Tests;
internal sealed class FakeNotificationService : INotificationService
{
    public List<(string Title, string Message)> Infos { get; } = [];
    public List<(string Title, string Message)> Warnings { get; } = [];
    public List<(string Title, string Message)> Successes { get; } = [];

    public Task ShowInfoAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        Infos.Add((title, message));
        return Task.CompletedTask;
    }

    public Task ShowWarningAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        Warnings.Add((title, message));
        return Task.CompletedTask;
    }

    public Task ShowSuccessAsync(string title, string message, CancellationToken cancellationToken = default)
    {
        Successes.Add((title, message));
        return Task.CompletedTask;
    }
}

internal sealed class FakeTaskEventAlertDispatcher : ITaskEventAlertDispatcher, INotificationTestService
{
    public List<(string Source, string Reason)> SessionInvalidNotifications { get; } = [];

    public List<(string LibraryName, string SeatName)> GrabSucceededNotifications { get; } = [];

    public List<string> OccupyReReserveSucceededNotifications { get; } = [];

    public List<(string LibraryName, string SeatName, string? Day)> TomorrowReservationSucceededNotifications { get; } = [];

    public List<(string LibraryName, string SeatName)> GlobalLeakSucceededNotifications { get; } = [];

    public List<(string TaskName, string Reason)> TaskFailedNotifications { get; } = [];

    public TaskCompletionSource? GrabSucceededCompletion { get; set; }

    public Exception? NotifySessionInvalidException { get; set; }

    public Exception? NotifyGrabSucceededException { get; set; }

    public Exception? NotifyOccupyReReserveSucceededException { get; set; }

    public Exception? NotifyTomorrowReservationSucceededException { get; set; }

    public Exception? NotifyGlobalLeakSucceededException { get; set; }

    public Exception? NotifyTaskFailedException { get; set; }

    public List<EmailAlertChannelSettings> TestEmailRequests { get; } = [];

    public List<TelegramAlertChannelSettings> TestTelegramRequests { get; } = [];

    public List<LocalDesktopAlertSettings> TestLocalAlertRequests { get; } = [];

    public Exception? SendTestEmailException { get; set; }

    public Exception? SendTestTelegramException { get; set; }

    public Exception? SendTestLocalException { get; set; }

    public Task NotifySessionInvalidAsync(string source, string reason, CancellationToken cancellationToken = default)
    {
        if (NotifySessionInvalidException is not null)
        {
            throw NotifySessionInvalidException;
        }

        SessionInvalidNotifications.Add((source, reason));
        return Task.CompletedTask;
    }

    public Task NotifyGrabSucceededAsync(string libraryName, string seatName, CancellationToken cancellationToken = default)
    {
        if (NotifyGrabSucceededException is not null)
        {
            throw NotifyGrabSucceededException;
        }

        GrabSucceededNotifications.Add((libraryName, seatName));
        return GrabSucceededCompletion?.Task ?? Task.CompletedTask;
    }

    public Task NotifyOccupyReReserveSucceededAsync(string seatName, CancellationToken cancellationToken = default)
    {
        if (NotifyOccupyReReserveSucceededException is not null)
        {
            throw NotifyOccupyReReserveSucceededException;
        }

        OccupyReReserveSucceededNotifications.Add(seatName);
        return Task.CompletedTask;
    }

    public Task NotifyTomorrowReservationSucceededAsync(
        string libraryName,
        string seatName,
        string? day,
        CancellationToken cancellationToken = default)
    {
        if (NotifyTomorrowReservationSucceededException is not null)
        {
            throw NotifyTomorrowReservationSucceededException;
        }

        TomorrowReservationSucceededNotifications.Add((libraryName, seatName, day));
        return Task.CompletedTask;
    }

    public Task NotifyGlobalLeakSucceededAsync(string libraryName, string seatName, CancellationToken cancellationToken = default)
    {
        if (NotifyGlobalLeakSucceededException is not null)
        {
            throw NotifyGlobalLeakSucceededException;
        }

        GlobalLeakSucceededNotifications.Add((libraryName, seatName));
        return Task.CompletedTask;
    }

    public Task NotifyTaskFailedAsync(string taskName, string reason, CancellationToken cancellationToken = default)
    {
        if (NotifyTaskFailedException is not null)
        {
            throw NotifyTaskFailedException;
        }

        TaskFailedNotifications.Add((taskName, reason));
        return Task.CompletedTask;
    }

    public Task SendTestEmailAsync(EmailAlertChannelSettings settings, CancellationToken cancellationToken = default)
    {
        if (SendTestEmailException is not null)
        {
            throw SendTestEmailException;
        }

        TestEmailRequests.Add(settings);
        return Task.CompletedTask;
    }

    public Task SendTestTelegramAsync(TelegramAlertChannelSettings settings, CancellationToken cancellationToken = default)
    {
        if (SendTestTelegramException is not null)
        {
            throw SendTestTelegramException;
        }

        TestTelegramRequests.Add(settings);
        return Task.CompletedTask;
    }

    public Task SendTestLocalAlertAsync(LocalDesktopAlertSettings settings, CancellationToken cancellationToken = default)
    {
        if (SendTestLocalException is not null)
        {
            throw SendTestLocalException;
        }

        TestLocalAlertRequests.Add(settings);
        return Task.CompletedTask;
    }
}

internal sealed class FakeCoordinatorEventPublisher : ICoordinatorEventPublisher
{
    public List<CoordinatorEvent> Events { get; } = [];

    public TaskCompletionSource? GrabSucceededCompletion { get; set; }

    public Exception? PublishException { get; set; }

    public Task PublishAsync(CoordinatorEvent @event, CancellationToken cancellationToken = default)
    {
        if (PublishException is not null)
        {
            throw PublishException;
        }

        Events.Add(@event);
        if (@event is GrabSucceededCoordinatorEvent && GrabSucceededCompletion is not null)
        {
            return GrabSucceededCompletion.Task;
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<TEvent> EventsOf<TEvent>()
        where TEvent : CoordinatorEvent
    {
        return Events.OfType<TEvent>().ToArray();
    }
}

internal sealed class FakeEmailAlertSender : IEmailAlertSender
{
    public List<(EmailAlertChannelSettings Settings, string Subject, string Body)> Requests { get; } = [];

    public Exception? SendException { get; set; }

    public Task SendAsync(
        EmailAlertChannelSettings settings,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        if (SendException is not null)
        {
            throw SendException;
        }

        Requests.Add((settings, subject, body));
        return Task.CompletedTask;
    }
}

internal sealed class FakeTelegramAlertSender : ITelegramAlertSender
{
    public List<(TelegramAlertChannelSettings Settings, string Message)> Requests { get; } = [];

    public Exception? SendException { get; set; }

    public TaskCompletionSource? SendCompletion { get; set; }

    public Task SendAsync(
        TelegramAlertChannelSettings settings,
        string message,
        CancellationToken cancellationToken = default)
    {
        if (SendException is not null)
        {
            throw SendException;
        }

        Requests.Add((settings, message));
        return SendCompletion?.Task ?? Task.CompletedTask;
    }
}

internal sealed class FakeSmtpTransportClient : ISmtpTransportClient
{
    public List<(string Host, int Port, SecureSocketOptions Options)> ConnectRequests { get; } = [];

    public List<(string Username, string Password)> AuthenticationRequests { get; } = [];

    public List<MimeMessage> SentMessages { get; } = [];

    public int DisconnectCalls { get; private set; }

    public bool Disposed { get; private set; }

    public Task ConnectAsync(string host, int port, SecureSocketOptions options, CancellationToken cancellationToken = default)
    {
        ConnectRequests.Add((host, port, options));
        return Task.CompletedTask;
    }

    public Task AuthenticateAsync(string username, string password, CancellationToken cancellationToken = default)
    {
        AuthenticationRequests.Add((username, password));
        return Task.CompletedTask;
    }

    public Task SendAsync(MimeMessage message, CancellationToken cancellationToken = default)
    {
        SentMessages.Add(message);
        return Task.CompletedTask;
    }

    public Task DisconnectAsync(bool quit, CancellationToken cancellationToken = default)
    {
        DisconnectCalls++;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Disposed = true;
        return ValueTask.CompletedTask;
    }
}

internal sealed class FakeSmtpTransportClientFactory(FakeSmtpTransportClient client) : ISmtpTransportClientFactory
{
    public FakeSmtpTransportClient Client { get; } = client;

    public ISmtpTransportClient Create() => Client;
}
