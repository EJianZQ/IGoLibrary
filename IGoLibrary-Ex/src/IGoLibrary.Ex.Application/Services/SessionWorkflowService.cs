using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class SessionWorkflowService(
    ITraceIntApiClient apiClient,
    ISessionService sessionService) : ISessionWorkflowService
{
    public async Task<SessionWorkflowResult> AuthenticateFromCodeAsync(
        string code,
        bool remember,
        CancellationToken cancellationToken = default)
    {
        var cookie = await apiClient.GetCookieFromCodeAsync(code, cancellationToken);
        var expirationTime = GetCookieExpirationTime(cookie);

        try
        {
            var session = await sessionService.AuthenticateFromCookieAsync(cookie, remember, cancellationToken);
            return BuildAuthenticatedResult(session, cookie, expirationTime);
        }
        catch (Exception ex)
        {
            return new SessionWorkflowResult(
                Session: null,
                Cookie: cookie,
                CookieExpirationTime: expirationTime,
                ShouldLoadLibraries: false,
                StatusMessage: "已获取 Cookie，等待验证",
                AuthenticationFailureMessage: ex.Message);
        }
    }

    public async Task<SessionWorkflowResult> AuthenticateFromCookieAsync(
        string cookie,
        bool remember,
        CancellationToken cancellationToken = default)
    {
        var session = await sessionService.AuthenticateFromCookieAsync(cookie, remember, cancellationToken);
        return BuildAuthenticatedResult(session, session.Cookie, GetCookieExpirationTime(session.Cookie));
    }

    public async Task<SessionWorkflowResult> RestoreAsync(CancellationToken cancellationToken = default)
    {
        var session = await sessionService.RestoreAsync(cancellationToken);
        if (session is null)
        {
            return new SessionWorkflowResult(
                Session: null,
                Cookie: null,
                CookieExpirationTime: null,
                ShouldLoadLibraries: false,
                StatusMessage: "本地没有可恢复的会话。");
        }

        return new SessionWorkflowResult(
            session,
            session.Cookie,
            GetCookieExpirationTime(session.Cookie),
            ShouldLoadLibraries: true,
            StatusMessage: $"已恢复会话：{session.Source} / {session.SavedAt:yyyy-MM-dd HH:mm:ss}");
    }

    public async Task<CookieValidationSnapshot> ValidateCurrentCookieAsync(CancellationToken cancellationToken = default)
    {
        var checkedAt = DateTimeOffset.Now;
        var session = sessionService.CurrentSession;
        if (session is null)
        {
            return CookieValidationSnapshot.Unknown(checkedAt);
        }

        var inferredExpirationTime = GetCookieExpirationTime(session.Cookie);
        try
        {
            await apiClient.ValidateCookieAsync(session.Cookie, cancellationToken);
            return CookieValidationSnapshot.Valid(checkedAt, inferredExpirationTime);
        }
        catch (Exception ex) when (SessionAuthFailureDetector.IsSessionInvalidException(ex))
        {
            return CookieValidationSnapshot.Invalid(checkedAt, ex.Message, inferredExpirationTime);
        }
        catch (Exception ex)
        {
            return CookieValidationSnapshot.CheckFailed(checkedAt, ex.Message, inferredExpirationTime);
        }
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        return sessionService.SignOutAsync(cancellationToken);
    }

    private static SessionWorkflowResult BuildAuthenticatedResult(
        SessionCredentials session,
        string cookie,
        DateTimeOffset? expirationTime)
    {
        return new SessionWorkflowResult(
            session,
            cookie,
            expirationTime,
            ShouldLoadLibraries: true,
            StatusMessage: $"登录成功：{session.Source} / {session.SavedAt:yyyy-MM-dd HH:mm:ss}");
    }

    private static DateTimeOffset? GetCookieExpirationTime(string? cookie)
    {
        return SessionAuthFailureDetector.TryGetCookieExpirationTime(cookie, out var expirationTime)
            ? expirationTime
            : null;
    }
}
