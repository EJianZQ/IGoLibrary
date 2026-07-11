namespace IGoLibrary.Ex.Desktop.Services;

internal interface ICloudflareQuickTunnelRunner
{
    Task<ICloudflareQuickTunnelSession> StartAsync(
        Uri originBaseUri,
        string healthCheckPath,
        CloudflareTunnelProxyOptions proxyOptions,
        ClashMihomoCompatibilityOptions compatibilityOptions,
        CancellationToken cancellationToken = default);
}

internal interface ICloudflareQuickTunnelSession : IAsyncDisposable
{
    Uri PublicBaseUri { get; }

    Task<CloudflareTunnelFault?> Completion { get; }
}

internal sealed record CloudflareTunnelFault(string Message);

internal sealed class CloudflareTunnelHealthState(int failureThreshold)
{
    public int ConsecutiveFailures { get; private set; }

    public bool RecordProbe(bool healthy)
    {
        ConsecutiveFailures = healthy ? 0 : ConsecutiveFailures + 1;
        return ConsecutiveFailures >= failureThreshold;
    }
}
