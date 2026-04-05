namespace IGoLibrary.Ex.Application.Exceptions;

public sealed class TraceIntApiException : InvalidOperationException
{
    public TraceIntApiException(
        string message,
        int? errorCode = null,
        string? remoteMessage = null,
        bool isAuthorizationDenied = false,
        Exception? innerException = null)
        : base(FormatMessage(message, errorCode), innerException)
    {
        ErrorCode = errorCode;
        RemoteMessage = remoteMessage ?? message;
        IsAuthorizationDenied = isAuthorizationDenied;
    }

    public int? ErrorCode { get; }

    public string RemoteMessage { get; }

    public bool IsAuthorizationDenied { get; }

    private static string FormatMessage(string message, int? errorCode)
    {
        return errorCode is int code
            ? $"GraphQL 错误(code={code}): {message}"
            : $"GraphQL 错误: {message}";
    }
}
