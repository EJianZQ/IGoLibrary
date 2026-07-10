using System.Net;
using System.Net.Http.Headers;
using IGoLibrary.Ex.Application.Exceptions;

namespace IGoLibrary.Ex.Infrastructure.Api;

internal sealed class TraceIntRemoteCheckInTransport(
    HttpClient httpClient,
    TraceIntRequestPolicy requestPolicy)
{
    private const string AuthUserAgent = "Mozilla/5.0 (iPad; CPU OS 27_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148 MicroMessenger/8.0.75(0x18004b21) NetType/WIFI Language/zh_CN miniProgram/wx3b9352e6b254ed2b";
    private const string ApiUserAgent = "Mozilla/5.0 (iPad; CPU OS 27_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Mobile/15E148 MicroMessenger/8.0.75(0x18004b21) NetType/WIFI Language/zh_CN";

    public Task<HttpResponseMessage> SendAuthorizationAsync(
        string requestUrl,
        string refererUrl,
        CancellationToken cancellationToken)
    {
        return requestPolicy.ExecuteOnceAsync(async requestToken =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUrl);
            ApplyHeaders(request, AuthUserAgent, refererUrl);
            return await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken);
        }, "获取签到授权", cancellationToken);
    }

    public Task<HttpResponseMessage> SendDevicesAsync(
        string endpointUrl,
        string refererUrl,
        string sessionToken,
        CancellationToken cancellationToken)
    {
        return requestPolicy.ExecuteAsync(async requestToken =>
        {
            using var request = CreateApiRequest(HttpMethod.Post, endpointUrl, refererUrl);
            request.Content = new FormUrlEncodedContent(new Dictionary<string, string> { ["t"] = sessionToken });
            return await SendAndEnsureSuccessAsync(request, requestToken);
        }, "获取签到信标", cancellationToken);
    }

    public Task<HttpResponseMessage> SendServerTimeAsync(
        string endpointUrl,
        string refererUrl,
        CancellationToken cancellationToken)
    {
        return requestPolicy.ExecuteAsync(async requestToken =>
        {
            using var request = CreateApiRequest(HttpMethod.Get, endpointUrl, refererUrl);
            return await SendAndEnsureSuccessAsync(request, requestToken);
        }, "获取服务器时间", cancellationToken);
    }

    public async Task<HttpResponseMessage> SendSignAsync(
        string endpointUrl,
        string refererUrl,
        IReadOnlyDictionary<string, string> form,
        CancellationToken cancellationToken)
    {
        try
        {
            return await requestPolicy.ExecuteOnceAsync(async requestToken =>
            {
                using var request = CreateApiRequest(HttpMethod.Post, endpointUrl, refererUrl);
                request.Content = new FormUrlEncodedContent(form);
                return await SendAndEnsureSuccessAsync(request, requestToken);
            }, "提交签到", cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (RemoteCheckInApiException)
        {
            throw;
        }
        catch (TimeoutException ex)
        {
            throw new RemoteCheckInOutcomeUnknownException(
                "签到请求的结果未知，请先核对预约状态，不要立即重复提交。",
                ex);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is null)
        {
            throw new RemoteCheckInOutcomeUnknownException(
                "签到请求的结果未知，请先核对预约状态，不要立即重复提交。",
                ex);
        }
    }

    private HttpRequestMessage CreateApiRequest(HttpMethod method, string url, string refererUrl)
    {
        var request = new HttpRequestMessage(method, url);
        ApplyHeaders(request, ApiUserAgent, refererUrl);
        return request;
    }

    private async Task<HttpResponseMessage> SendAndEnsureSuccessAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.SendAsync(request, cancellationToken);
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            throw new RemoteCheckInApiException("签到授权已失效，请重新扫码授权。", isSessionInvalid: true);
        }

        try
        {
            response.EnsureSuccessStatusCode();
            return response;
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    private static void ApplyHeaders(HttpRequestMessage request, string userAgent, string referer)
    {
        request.Version = HttpVersion.Version11;
        request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
        request.Headers.TryAddWithoutValidation("User-Agent", userAgent);
        request.Headers.Referrer = new Uri(referer);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
        request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9");
    }
}
