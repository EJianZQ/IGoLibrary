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
internal sealed class FakeTraceIntApiClient : ITraceIntApiClient
{
    public Func<string, CancellationToken, Task<string>>? OnGetCookieFromCodeAsync { get; set; }
    public Func<string, CancellationToken, Task>? OnValidateCookieAsync { get; set; }
    public Func<string, CancellationToken, Task<IReadOnlyList<LibrarySummary>>>? OnGetLibrariesAsync { get; set; }
    public Func<string, int, CancellationToken, Task<LibraryLayout>>? OnGetLibraryLayoutAsync { get; set; }
    public Func<string, int, CancellationToken, Task<LibraryRule>>? OnGetLibraryRuleAsync { get; set; }
    public Func<string, CancellationToken, Task<ReservationInfo?>>? OnGetReservationInfoAsync { get; set; }
    public Func<string, int, string, CancellationToken, Task<bool>>? OnReserveSeatAsync { get; set; }
    public Func<string, string, CancellationToken, Task<bool>>? OnCancelReservationAsync { get; set; }

    public Task<string> GetCookieFromCodeAsync(string code, CancellationToken cancellationToken = default)
        => OnGetCookieFromCodeAsync?.Invoke(code, cancellationToken) ?? Task.FromResult(string.Empty);

    public Task ValidateCookieAsync(string cookie, CancellationToken cancellationToken = default)
        => OnValidateCookieAsync?.Invoke(cookie, cancellationToken) ?? Task.CompletedTask;

    public Task<IReadOnlyList<LibrarySummary>> GetLibrariesAsync(string cookie, CancellationToken cancellationToken = default)
        => OnGetLibrariesAsync?.Invoke(cookie, cancellationToken) ?? Task.FromResult<IReadOnlyList<LibrarySummary>>([]);

    public Task<LibraryLayout> GetLibraryLayoutAsync(string cookie, int libraryId, CancellationToken cancellationToken = default)
        => OnGetLibraryLayoutAsync?.Invoke(cookie, libraryId, cancellationToken)
           ?? throw new NotSupportedException();

    public Task<LibraryRule> GetLibraryRuleAsync(string cookie, int libraryId, CancellationToken cancellationToken = default)
        => OnGetLibraryRuleAsync?.Invoke(cookie, libraryId, cancellationToken)
           ?? throw new NotSupportedException();

    public Task<ReservationInfo?> GetReservationInfoAsync(string cookie, CancellationToken cancellationToken = default)
        => OnGetReservationInfoAsync?.Invoke(cookie, cancellationToken) ?? Task.FromResult<ReservationInfo?>(null);

    public Task<bool> ReserveSeatAsync(string cookie, int libraryId, string seatKey, CancellationToken cancellationToken = default)
        => OnReserveSeatAsync?.Invoke(cookie, libraryId, seatKey, cancellationToken) ?? Task.FromResult(false);

    public Task<bool> CancelReservationAsync(string cookie, string reservationToken, CancellationToken cancellationToken = default)
        => OnCancelReservationAsync?.Invoke(cookie, reservationToken, cancellationToken) ?? Task.FromResult(false);
}

internal sealed class FakeProtocolTemplateStore(TraceIntGraphQlTemplateSet templates) : IProtocolTemplateStore
{
    public TraceIntGraphQlTemplateSet Templates { get; private set; } = templates;

    public Task<TraceIntGraphQlTemplateSet> GetEffectiveTemplatesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(Templates);

    public Task SaveOverridesAsync(TraceIntGraphQlTemplateOverrides overrides, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ResetOverridesAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed class SequenceHttpMessageHandler(params Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>[] steps)
    : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _steps = new(steps);

    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        if (_steps.Count == 0)
        {
            throw new InvalidOperationException("没有更多预设响应。");
        }

        return _steps.Dequeue().Invoke(request, cancellationToken);
    }

    public static Task<HttpResponseMessage> JsonResponseAsync(string json)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json)
        });
    }
}
