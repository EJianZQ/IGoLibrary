namespace IGoLibrary.Ex.Desktop.Services;

internal interface ICloudflareQuickTunnelRunner
{
    Task ValidateConfigurationAsync(
        CloudflareTunnelProxyOptions proxyOptions,
        ClashMihomoCompatibilityOptions compatibilityOptions,
        CancellationToken cancellationToken = default);

    Task<ICloudflareQuickTunnelSession> StartAsync(
        Uri originBaseUri,
        string healthCheckPath,
        CloudflareTunnelProxyOptions proxyOptions,
        ClashMihomoCompatibilityOptions compatibilityOptions,
        CancellationToken cancellationToken = default);
}

internal sealed class CloudflaredUnavailableException : FileNotFoundException
{
    internal const string UserMessage =
        "当前安装未包含 cloudflared，无法使用 Cloudflare Tunnel。" +
        "请到“系统设置 → 网络与接口”重新选择 Cloudflare Tunnel，并按提示下载";

    public CloudflaredUnavailableException(string executablePath)
        : base(UserMessage, executablePath)
    {
    }
}

internal sealed class CloudflareTunnelProxyConflictException : InvalidOperationException
{
    internal const string UserMessage =
        "检测到系统已开启代理，但Clash/Mihomo 路由策略为 DIRECT，明显冲突" +
        "请填写正确的路由策略或把代理方式切换为不使用显式代理";

    public CloudflareTunnelProxyConflictException()
        : base(UserMessage)
    {
    }
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
