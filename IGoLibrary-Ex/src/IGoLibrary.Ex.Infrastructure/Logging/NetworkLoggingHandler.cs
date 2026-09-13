using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.Logging;

public sealed class NetworkLoggingHandler(NetworkTrafficLogger logger, string service = "HTTP") : DelegatingHandler
{
    public static HttpMessageHandler Wrap(HttpMessageHandler inner, NetworkTrafficLogger? logger, string service) =>
        logger is null ? inner : new NetworkLoggingHandler(logger, service) { InnerHandler = inner };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var session = logger.Start(service, "出站", request.Method.Method, request.RequestUri?.ToString());
        if (session is null) return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

        NetworkCaptureContent? requestCapture = null;
        if (request.Content is { } content)
            request.Content = requestCapture = new NetworkCaptureContent(content, session,
                (body, failed) => session.Write($"请求体；{body}", failed ? LogLevel.Warning : LogLevel.Information));
        else session.Write("请求体；无正文");

        try
        {
            var response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            requestCapture?.Finish();
            var level = response.IsSuccessStatusCode ? LogLevel.Information : LogLevel.Warning;
            session.Write($"收到响应头；状态码={(int)response.StatusCode}；最终地址={NetworkLogSanitizer.Address(response.RequestMessage?.RequestUri?.ToString(), service)}", level);
            response.Content = new NetworkCaptureContent(response.Content, session,
                (body, failed) => session.Write($"响应结束；状态码={(int)response.StatusCode}；{body}", failed ? LogLevel.Warning : level));
            return response;
        }
        catch (Exception ex)
        {
            requestCapture?.Finish();
            session.Write("响应；未收到响应");
            session.Failure(ex);
            throw;
        }
    }
}
