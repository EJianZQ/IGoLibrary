using IGoLibrary.Ex.Infrastructure.Logging;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal static class NetworkLoggingMiddleware
{
    public static void UseNetworkLogging(this WebApplication app, NetworkTrafficLogger? logger, string service, string token)
    {
        if (logger is null) return;
        app.Use((context, next) => InvokeAsync(context, next, logger, service, token));
    }

    internal static async Task InvokeAsync(HttpContext context, RequestDelegate next, NetworkTrafficLogger logger, string service, string token)
    {
        var request = context.Request;
        var session = logger.Start(service, "入站", request.Method,
            $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}{request.QueryString}");
        if (session is null) { await next(context); return; }
        var input = request.Body;
        var output = context.Response.Body;
        var requestBody = session.CreateBody(() => NetworkLogSanitizer.IsText(request.ContentType));
        var responseBody = session.CreateBody(() => NetworkLogSanitizer.IsText(context.Response.ContentType));
        using var inputObserver = new NetworkCaptureStream(input, requestBody, reading: true);
        using var outputObserver = new NetworkCaptureStream(output, responseBody, reading: false);
        request.Body = inputObserver;
        context.Response.Body = outputObserver;
        context.Response.OnStarting(() =>
        {
            session.Write($"收到响应头；状态码={context.Response.StatusCode}");
            return Task.CompletedTask;
        });
        try
        {
            await next(context);
            responseBody.Complete();
            if (request.ContentLength == 0) requestBody.Complete();
        }
        catch (Exception ex)
        {
            responseBody.Fail();
            session.Failure(ex);
            throw;
        }
        finally
        {
            request.Body = input;
            context.Response.Body = output;
            session.Write($"请求体；{requestBody.Render(request.ContentType, request.ContentLength, [token])}");
            session.Write($"响应结束；状态码={context.Response.StatusCode}；{responseBody.Render(context.Response.ContentType, context.Response.ContentLength, [token])}",
                context.Response.StatusCode >= 400 ? LogLevel.Warning : LogLevel.Information);
        }
    }
}
