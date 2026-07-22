using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Infrastructure.Security;

public sealed class InMemoryCredentialStore(IPersistentDataChangeTracker? changeTracker = null) : ICredentialStore
{
    private SessionCredentials? _session;
    private RemoteCheckInSessionCredentials? _remoteCheckInSession;

    public Task SaveSessionAsync(SessionCredentials credentials, CancellationToken cancellationToken = default)
    {
        _session = credentials;
        changeTracker?.MarkChanged();
        return Task.CompletedTask;
    }

    public Task<SessionCredentials?> LoadSessionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_session);
    }

    public Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        _session = null;
        changeTracker?.MarkChanged();
        return Task.CompletedTask;
    }

    public Task SaveRemoteCheckInSessionAsync(
        RemoteCheckInSessionCredentials credentials,
        CancellationToken cancellationToken = default)
    {
        _remoteCheckInSession = credentials;
        changeTracker?.MarkChanged();
        return Task.CompletedTask;
    }

    public Task<RemoteCheckInSessionCredentials?> LoadRemoteCheckInSessionAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_remoteCheckInSession);
    }

    public Task ClearRemoteCheckInSessionAsync(CancellationToken cancellationToken = default)
    {
        _remoteCheckInSession = null;
        changeTracker?.MarkChanged();
        return Task.CompletedTask;
    }
}
