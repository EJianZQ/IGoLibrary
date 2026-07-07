using IGoLibrary.Ex.Application.Configuration;

namespace IGoLibrary.Ex.Desktop.Services;

public interface IMobileControlService
{
    event EventHandler<MobileControlStoppedEventArgs>? Stopped;

    event EventHandler<MobileControlDeviceCountChangedEventArgs>? DeviceCountChanged;

    MobileControlSession? CurrentSession { get; }

    int ConnectedDeviceCount { get; }

    Task<MobileControlSession> StartAsync(
        MobileControlSettings settings,
        CancellationToken cancellationToken = default);

    Task StopAsync(
        MobileControlStopReason reason = MobileControlStopReason.Manual,
        CancellationToken cancellationToken = default);
}

public sealed record MobileControlSession(
    Guid SessionId,
    Uri Url,
    string Host,
    int Port,
    DateTimeOffset StartedAt);

public sealed class MobileControlDeviceCountChangedEventArgs(int connectedDeviceCount) : EventArgs
{
    public int ConnectedDeviceCount { get; } = connectedDeviceCount;
}

public sealed class MobileControlStoppedEventArgs(
    Guid sessionId,
    MobileControlStopReason reason,
    string? message = null) : EventArgs
{
    public Guid SessionId { get; } = sessionId;

    public MobileControlStopReason Reason { get; } = reason;

    public string? Message { get; } = message;
}

public enum MobileControlStopReason
{
    Manual,
    Replaced,
    Failed
}
