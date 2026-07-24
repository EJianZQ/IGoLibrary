using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Application.Services;

public sealed class SessionWorkflowService(
    ITraceIntApiClient apiClient,
    ISessionService sessionService,
    ILogger<SessionWorkflowService>? logger = null) : ISessionWorkflowService
{
    private readonly ILogger<SessionWorkflowService> _logger =
        logger ?? NullLogger<SessionWorkflowService>.Instance;

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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "扫码授权已取得 Cookie，但自动会话验证失败。记住会话={RememberSession}。",
                remember);
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
                StatusMessage: "本地没有可恢复的会话");
        }

        return new SessionWorkflowResult(
            session,
            session.Cookie,
            GetCookieExpirationTime(session.Cookie),
            ShouldLoadLibraries: true,
            StatusMessage: $"已恢复会话：{session.Source} / {session.SavedAt:yyyy-MM-dd HH:mm:ss}");
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
