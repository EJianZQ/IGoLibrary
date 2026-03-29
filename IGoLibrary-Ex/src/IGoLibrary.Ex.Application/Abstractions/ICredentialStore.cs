using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ICredentialStore
{
    Task SaveSessionAsync(SessionCredentials credentials, CancellationToken cancellationToken = default);

    Task<SessionCredentials?> LoadSessionAsync(CancellationToken cancellationToken = default);

    Task ClearSessionAsync(CancellationToken cancellationToken = default);
}
