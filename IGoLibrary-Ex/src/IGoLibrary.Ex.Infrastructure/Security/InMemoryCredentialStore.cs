using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Infrastructure.Security;

public sealed class InMemoryCredentialStore : ICredentialStore
{
    private SessionCredentials? _session;

    public Task SaveSessionAsync(SessionCredentials credentials, CancellationToken cancellationToken = default)
    {
        _session = credentials;
        return Task.CompletedTask;
    }

    public Task<SessionCredentials?> LoadSessionAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(_session);
    }

    public Task ClearSessionAsync(CancellationToken cancellationToken = default)
    {
        _session = null;
        return Task.CompletedTask;
    }
}
