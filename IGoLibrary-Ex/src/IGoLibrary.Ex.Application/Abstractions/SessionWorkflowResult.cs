using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public sealed record SessionWorkflowResult(
    SessionCredentials? Session,
    string? Cookie,
    DateTimeOffset? CookieExpirationTime,
    bool ShouldLoadLibraries,
    string StatusMessage,
    string? AuthenticationFailureMessage = null);
