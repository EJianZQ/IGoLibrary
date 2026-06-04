using System.Net;
using IGoLibrary.Ex.Application.Abstractions;
using RestSharp;

namespace IGoLibrary.Ex.Infrastructure.Api;

internal sealed class TraceIntCookieTransport(
    IProtocolTemplateStore protocolTemplateStore,
    TraceIntRequestPolicy requestPolicy,
    ITraceIntCookieHttpClient httpClient)
{
    public async Task<TraceIntCookieHttpResponse> GetCookieAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        var templates = await protocolTemplateStore.GetEffectiveTemplatesAsync(cancellationToken);
        var requestUrl = templates.GetCookieUrlTemplate.Replace("ReplaceMeByCode", code, StringComparison.Ordinal);

        return await requestPolicy.ExecuteAsync(async requestToken =>
        {
            TraceIntCookieHttpResponse result;
            try
            {
                result = await httpClient.ExecuteGetAsync(requestUrl, requestToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException and not HttpRequestException)
            {
                throw new HttpRequestException("获取 Cookie 请求失败，请检查网络连接或授权链接是否可访问。", ex);
            }

            if (result.Cookies?.Count >= 2)
            {
                return result;
            }

            if (IsRetriableCookieFailure(result.Response))
            {
                throw CreateRetryException(result.Response);
            }

            return result;
        }, "获取 Cookie", cancellationToken);
    }

    private static bool IsRetriableCookieFailure(RestResponse response)
    {
        return response.ResponseStatus != ResponseStatus.Completed ||
               TraceIntRequestPolicy.IsTransient(ToNullableStatusCode(response.StatusCode));
    }

    private static HttpRequestException CreateRetryException(RestResponse response)
    {
        var reason = response.ErrorMessage;
        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = response.StatusDescription;
        }

        if (string.IsNullOrWhiteSpace(reason) && response.ResponseStatus != ResponseStatus.Completed)
        {
            reason = response.ResponseStatus.ToString();
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            reason = "请检查授权链接是否过期或网络是否可用";
        }

        var statusCode = ToNullableStatusCode(response.StatusCode);
        return statusCode is null
            ? new HttpRequestException($"获取 Cookie 请求失败：{reason}", response.ErrorException, statusCode)
            : new HttpRequestException(
                $"获取 Cookie 请求失败，HTTP {(int)response.StatusCode} {response.StatusCode}：{reason}",
                response.ErrorException,
                statusCode);
    }

    private static HttpStatusCode? ToNullableStatusCode(HttpStatusCode statusCode)
    {
        return statusCode == 0 ? null : statusCode;
    }
}

internal sealed record TraceIntCookieHttpResponse(
    RestResponse Response,
    IReadOnlyList<string>? Cookies);

internal interface ITraceIntCookieHttpClient
{
    Task<TraceIntCookieHttpResponse> ExecuteGetAsync(string requestUrl, CancellationToken cancellationToken);
}

internal sealed class RestSharpTraceIntCookieHttpClient : ITraceIntCookieHttpClient
{
    public async Task<TraceIntCookieHttpResponse> ExecuteGetAsync(
        string requestUrl,
        CancellationToken cancellationToken)
    {
        using var client = new RestClient(requestUrl);
        var request = new RestRequest
        {
            Method = Method.Get
        };

        var response = await client.ExecuteAsync(request, cancellationToken);
        var responseCookies = response.Cookies?.Select(cookie => cookie.ToString()).ToArray();
        return new TraceIntCookieHttpResponse(response, responseCookies);
    }
}
