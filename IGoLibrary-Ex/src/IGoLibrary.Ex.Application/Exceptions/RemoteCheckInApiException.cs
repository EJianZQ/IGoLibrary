namespace IGoLibrary.Ex.Application.Exceptions;

public sealed class RemoteCheckInApiException : InvalidOperationException
{
    public RemoteCheckInApiException(
        string message,
        int? errorCode = null,
        string? remoteMessage = null,
        bool isSessionInvalid = false,
        Exception? innerException = null)
        : base(errorCode is int code ? $"签到接口错误(code={code})：{message}" : $"签到接口错误：{message}", innerException)
    {
        ErrorCode = errorCode;
        RemoteMessage = remoteMessage ?? message;
        IsSessionInvalid = isSessionInvalid;
    }

    public int? ErrorCode { get; }

    public string RemoteMessage { get; }

    public bool IsSessionInvalid { get; }
}

public sealed class RemoteCheckInOutcomeUnknownException : InvalidOperationException
{
    public RemoteCheckInOutcomeUnknownException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class RemoteCheckInAuthorizationException : InvalidOperationException
{
    public RemoteCheckInAuthorizationException(
        string message,
        bool isSessionInvalid,
        Exception? innerException = null)
        : base(message, innerException)
    {
        IsSessionInvalid = isSessionInvalid;
    }

    public bool OAuthCodeConsumed => true;

    public bool IsSessionInvalid { get; }
}
