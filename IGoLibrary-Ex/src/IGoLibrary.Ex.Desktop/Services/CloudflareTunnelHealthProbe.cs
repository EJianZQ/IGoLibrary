using System.Net;

namespace IGoLibrary.Ex.Desktop.Services;

internal interface ICloudflareTunnelHealthProbeFactory
{
    ICloudflareTunnelHealthProbeSession Create(Uri? proxyUri);
}

internal interface ICloudflareTunnelHealthProbeSession : IDisposable
{
    Task<bool> IsHealthyAsync(
        Uri healthCheckUri,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
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

internal sealed class CloudflareTunnelHealthProbeSession(SocketsHttpHandler handler)
    : ICloudflareTunnelHealthProbeSession
{
    private readonly HttpClient _httpClient = new(handler, disposeHandler: true);

    public async Task<bool> IsHealthyAsync(
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
            return response.StatusCode == HttpStatusCode.NoContent;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
