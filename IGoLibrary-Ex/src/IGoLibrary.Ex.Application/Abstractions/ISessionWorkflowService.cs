namespace IGoLibrary.Ex.Application.Abstractions;

public interface ISessionWorkflowService
{
    Task<SessionWorkflowResult> AuthenticateFromCodeAsync(
        string code,
        bool remember,
        CancellationToken cancellationToken = default);

    Task<SessionWorkflowResult> AuthenticateFromCookieAsync(
        string cookie,
        bool remember,
        CancellationToken cancellationToken = default);

    Task<SessionWorkflowResult> RestoreAsync(CancellationToken cancellationToken = default);

    Task SignOutAsync(CancellationToken cancellationToken = default);
}
