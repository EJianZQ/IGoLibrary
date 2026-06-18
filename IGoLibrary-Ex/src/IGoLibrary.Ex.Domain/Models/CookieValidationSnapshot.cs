using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record CookieValidationSnapshot(
    CookieValidationState State,
    DateTimeOffset CheckedAt,
    string? FailureReason,
    DateTimeOffset? InferredExpirationTime)
{
    public static CookieValidationSnapshot Unknown(
        DateTimeOffset checkedAt,
        DateTimeOffset? inferredExpirationTime = null,
        string? failureReason = null)
    {
        return new CookieValidationSnapshot(
            CookieValidationState.Unknown,
            checkedAt,
            failureReason,
            inferredExpirationTime);
    }

    public static CookieValidationSnapshot Valid(
        DateTimeOffset checkedAt,
        DateTimeOffset? inferredExpirationTime)
    {
        return new CookieValidationSnapshot(
            CookieValidationState.Valid,
            checkedAt,
            null,
            inferredExpirationTime);
    }

    public static CookieValidationSnapshot Invalid(
        DateTimeOffset checkedAt,
        string failureReason,
        DateTimeOffset? inferredExpirationTime)
    {
        return new CookieValidationSnapshot(
            CookieValidationState.Invalid,
            checkedAt,
            failureReason,
            inferredExpirationTime);
    }

    public static CookieValidationSnapshot CheckFailed(
        DateTimeOffset checkedAt,
        string failureReason,
        DateTimeOffset? inferredExpirationTime)
    {
        return new CookieValidationSnapshot(
            CookieValidationState.CheckFailed,
            checkedAt,
            failureReason,
            inferredExpirationTime);
    }
}
