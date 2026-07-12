using System.Net;

namespace IGoLibrary.Ex.Desktop.Services;

internal interface ICloudflareTunnelHealthProbeFactory
{
    ICloudflareTunnelHealthProbeSession Create(Uri? proxyUri);
}

internal interface ICloudflareTunnelHealthProbeSession : IDisposable
{
    Task<CloudflareTunnelHealthProbeResult> ProbeAsync(
        Uri healthCheckUri,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}

internal sealed record CloudflareTunnelHealthProbeResult(
    bool IsHealthy,
    Exception? Failure)
{
    public static CloudflareTunnelHealthProbeResult Healthy { get; } = new(true, null);

    public static CloudflareTunnelHealthProbeResult Failed(Exception failure) => new(false, failure);
}

internal sealed class CloudflareTunnelHealthProbeFactory : ICloudflareTunnelHealthProbeFactory
{
    public ICloudflareTunnelHealthProbeSession Create(Uri? proxyUri)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = proxyUri is not null,
            Proxy = proxyUri is null ? null : new WebProxy(proxyUri)
        };
        return new CloudflareTunnelHealthProbeSession(handler);
    }
}

internal sealed class CloudflareTunnelHealthProbeSession(HttpMessageHandler handler)
    : ICloudflareTunnelHealthProbeSession
{
    private readonly HttpClient _httpClient = new(handler, disposeHandler: true);

    public async Task<CloudflareTunnelHealthProbeResult> ProbeAsync(
        Uri healthCheckUri,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, healthCheckUri);
        request.Headers.CacheControl = new System.Net.Http.Headers.CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true
        };

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutSource.Token);
            return response.StatusCode == HttpStatusCode.NoContent
                ? CloudflareTunnelHealthProbeResult.Healthy
                : CloudflareTunnelHealthProbeResult.Failed(new HttpRequestException(
                    $"Cloudflare Tunnel 健康检查返回 HTTP {(int)response.StatusCode} ({response.StatusCode})",
                    inner: null,
                    response.StatusCode));
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            return CloudflareTunnelHealthProbeResult.Failed(new TimeoutException(
                $"Cloudflare Tunnel 健康检查在 {timeout.TotalSeconds:0.###} 秒内超时",
                ex));
        }
        catch (HttpRequestException ex)
        {
            return CloudflareTunnelHealthProbeResult.Failed(ex);
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
