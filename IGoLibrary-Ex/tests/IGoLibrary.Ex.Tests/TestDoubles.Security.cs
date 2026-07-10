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

    public RemoteCheckInSessionCredentials? StoredRemoteCheckInSession { get; set; }

    public int SaveCalls { get; private set; }

    public int ClearCalls { get; private set; }

    public int SaveRemoteCheckInCalls { get; private set; }

    public int ClearRemoteCheckInCalls { get; private set; }

    public Exception? ClearException { get; set; }

    public Exception? LoadException { get; set; }

    public Exception? SaveRemoteCheckInException { get; set; }

    public Exception? ClearRemoteCheckInException { get; set; }

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

    public Task SaveRemoteCheckInSessionAsync(
        RemoteCheckInSessionCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        if (SaveRemoteCheckInException is not null)
        {
            throw SaveRemoteCheckInException;
        }

        SaveRemoteCheckInCalls++;
        StoredRemoteCheckInSession = credentials;
        return Task.CompletedTask;
    }

    public Task<RemoteCheckInSessionCredentials?> LoadRemoteCheckInSessionAsync(
        CancellationToken cancellationToken = default)
    {
        if (LoadException is not null)
        {
            throw LoadException;
        }

        return Task.FromResult(StoredRemoteCheckInSession);
    }

    public Task ClearRemoteCheckInSessionAsync(CancellationToken cancellationToken = default)
    {
        if (ClearRemoteCheckInException is not null)
        {
            throw ClearRemoteCheckInException;
        }

        if (ClearException is not null)
        {
            throw ClearException;
        }

        ClearRemoteCheckInCalls++;
        StoredRemoteCheckInSession = null;
        return Task.CompletedTask;
    }
}
