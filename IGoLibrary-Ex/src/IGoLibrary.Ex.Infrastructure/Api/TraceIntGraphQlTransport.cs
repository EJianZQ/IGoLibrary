using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace IGoLibrary.Ex.Infrastructure.Api;

internal sealed class TraceIntGraphQlTransport(
    HttpClient httpClient,
    TraceIntRequestPolicy requestPolicy)
{
    private const string DesktopUserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/81.0.4044.138 Safari/537.36 NetType/WIFI MicroMessenger/7.0.20.1781(0x6700143B) WindowsWechat(0x63070626)";
    private const string AppVersion = "2.0.11";

    public async Task<HttpResponseMessage> SendAsync(string cookie, string payload, CancellationToken cancellationToken)
    {
        return await requestPolicy.ExecuteAsync(async requestToken =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://wechat.v2.traceint.com/index.php/graphql/");
            request.Version = HttpVersion.Version11;
            request.VersionPolicy = HttpVersionPolicy.RequestVersionOrLower;
            request.Headers.Host = "wechat.v2.traceint.com";
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            request.Headers.TryAddWithoutValidation("Connection", "keep-alive");
            request.Headers.TryAddWithoutValidation("Origin", "https://web.traceint.com");
            request.Headers.TryAddWithoutValidation("Referer", "https://web.traceint.com/web/index.html");
            request.Headers.TryAddWithoutValidation("User-Agent", DesktopUserAgent);
            request.Headers.TryAddWithoutValidation("App-Version", AppVersion);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));
            request.Headers.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
            request.Headers.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en-US;q=0.8,en;q=0.7");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Site", "same-site");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Mode", "cors");
            request.Headers.TryAddWithoutValidation("Sec-Fetch-Dest", "empty");
            request.Headers.ExpectContinue = false;

            var payloadBytes = Encoding.UTF8.GetBytes(payload);
            request.Content = new ByteArrayContent(payloadBytes);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request.Content.Headers.ContentLength = payloadBytes.Length;

            var response = await httpClient.SendAsync(request, requestToken);
            response.EnsureSuccessStatusCode();
            return response;
        }, "请求", cancellationToken);
    }
}
