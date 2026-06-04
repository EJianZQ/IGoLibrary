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
internal sealed class FakeCredentialStore : ICredentialStore
{
    public SessionCredentials? StoredSession { get; set; }

    public int SaveCalls { get; private set; }

    public int ClearCalls { get; private set; }

    public Exception? ClearException { get; set; }

    public Exception? LoadException { get; set; }

    public Task SaveSessionAsync(SessionCredentials credentials, CancellationToken cancellationToken = default)
    {
        SaveCalls++;
        StoredSession = credentials;
        return Task.CompletedTask;
    }

    public Task<SessionCredentials?> LoadSessionAsync(CancellationToken cancellationToken = default)
    {
        if (LoadException is not null)
        {
            throw LoadException;
        }

        return Task.FromResult(StoredSession);
    }

    public Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        if (ClearException is not null)
        {
            throw ClearException;
        }

        ClearCalls++;
        StoredSession = null;
        return Task.CompletedTask;
    }
}
