using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ISessionService
{
    SessionCredentials? CurrentSession { get; }

    Task<SessionCredentials> AuthenticateFromCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<SessionCredentials> AuthenticateFromCookieAsync(string cookie, bool remember, CancellationToken cancellationToken = default);

    Task<SessionCredentials?> RestoreAsync(CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);
}
