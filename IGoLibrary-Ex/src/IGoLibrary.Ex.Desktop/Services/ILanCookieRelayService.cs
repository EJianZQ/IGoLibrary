namespace IGoLibrary.Ex.Desktop.Services;

public interface ILanCookieRelayService
{
    event EventHandler<LanCookieRelayStoppedEventArgs>? Stopped;

    Task<LanCookieRelaySession> StartAsync(
        Func<string, CancellationToken, Task<LanCookieRelaySubmitResult>> submitHandler,
        CancellationToken cancellationToken = default);

    Task StopAsync(
        LanCookieRelayStopReason reason = LanCookieRelayStopReason.Manual,
        CancellationToken cancellationToken = default);
}

public sealed record LanCookieRelaySession(
    Guid SessionId,
    Uri Url,
    string Host,
    int Port,
    DateTimeOffset StartedAt,
    TimeSpan Timeout);

public sealed record LanCookieRelaySubmitResult(bool Success, string Message)
{
    public static LanCookieRelaySubmitResult Succeeded(string message)
    {
        return new LanCookieRelaySubmitResult(true, message);
    }

    public static LanCookieRelaySubmitResult Failed(string message)
    {
        return new LanCookieRelaySubmitResult(false, message);
    }
}

public sealed class LanCookieRelayStoppedEventArgs(
    Guid sessionId,
    LanCookieRelayStopReason reason,
    string? message = null) : EventArgs
{
    public Guid SessionId { get; } = sessionId;

    public LanCookieRelayStopReason Reason { get; } = reason;

    public string? Message { get; } = message;
}

public enum LanCookieRelayStopReason
{
    Manual,
    Replaced,
    Submitted,
    Timeout,
    Failed
}
