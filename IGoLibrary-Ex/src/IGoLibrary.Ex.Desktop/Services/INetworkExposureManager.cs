using IGoLibrary.Ex.Application.Configuration;

namespace IGoLibrary.Ex.Desktop.Services;

public interface INetworkExposureManager : IAsyncDisposable
{
    event EventHandler<NetworkModeChangedEventArgs>? ModeChanged;

    MobileControlNetworkMode CurrentMode { get; }

    void Initialize(
        MobileControlNetworkMode networkMode,
        CloudflareTunnelProxyMode tunnelProxyMode,
        string tunnelManualProxyUrl,
        bool clashMihomoCompatibilityEnabled = false,
        string clashMihomoConfigPath = "",
        string clashMihomoRoutePolicy = "DIRECT",
        bool fallbackToLocalNetworkOnTunnelFailure = true);

    Task<MobileControlNetworkMode> SetModeAsync(
        MobileControlNetworkMode networkMode,
        CancellationToken cancellationToken = default);

    Task<MobileControlSettings> SetCloudflareTunnelProxyAsync(
        CloudflareTunnelProxyMode proxyMode,
        string manualProxyUrl,
        CancellationToken cancellationToken = default);

    Task<MobileControlSettings> SetCloudflareTunnelFallbackAsync(
        bool fallbackToLocalNetworkOnTunnelFailure,
        CancellationToken cancellationToken = default);

    Task<MobileControlSettings> SetClashMihomoCompatibilityAsync(
        bool enabled,
        string configPath,
        string routePolicy,
        CancellationToken cancellationToken = default);

    Task<INetworkExposureLease> PublishAsync(
        Uri lanUrl,
        string healthCheckPath,
        CancellationToken cancellationToken = default);
}

public interface INetworkExposureLease : IAsyncDisposable
{
    event EventHandler<NetworkExposureChangedEventArgs>? EndpointChanged;

    Guid Id { get; }

    Uri LanUrl { get; }

    Uri Url { get; }

    MobileControlNetworkMode EffectiveMode { get; }
}

public sealed class NetworkModeChangedEventArgs(
    MobileControlNetworkMode mode,
    string? message = null) : EventArgs
{
    public MobileControlNetworkMode Mode { get; } = mode;

    public string? Message { get; } = message;
}

public sealed class NetworkExposureChangedEventArgs(
    Uri url,
    MobileControlNetworkMode effectiveMode) : EventArgs
{
    public Uri Url { get; } = url;

    public MobileControlNetworkMode EffectiveMode { get; } = effectiveMode;
}
