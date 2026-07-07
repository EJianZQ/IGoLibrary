using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class LanCookieRelayService(
    ILanAddressProvider addressProvider,
    ILogger<LanCookieRelayService> logger) : ILanCookieRelayService, IAsyncDisposable
{
    private static readonly TimeSpan DefaultSessionTimeout = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WebApplication? _app;
    private CancellationTokenSource? _timeoutCts;
    private LanCookieRelaySession? _session;
    private int _terminalSubmissionStarted;

    public event EventHandler<LanCookieRelayStoppedEventArgs>? Stopped;

    public async Task<LanCookieRelaySession> StartAsync(
        Func<string, CancellationToken, Task<LanCookieRelaySubmitResult>> submitHandler,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(submitHandler);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopCurrentSessionAsync(LanCookieRelayStopReason.Replaced, null, cancellationToken);

            var address = addressProvider.GetPrimaryLanAddress()
                ?? throw new InvalidOperationException("没有找到可用的局域网 IPv4 地址，请确认电脑已连接到局域网");
            var token = CreateToken();
            var sessionId = Guid.NewGuid();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(LanCookieRelayService).Assembly.GetName().Name
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(address, 0);
                options.Limits.MaxRequestBodySize = SubmittedLinkReader.MaxRequestBodyBytes;
            });

            var app = builder.Build();
            app.MapGet("/", context => WriteLandingPageAsync(context, token));
            app.MapPost("/submit", context => HandleSubmitAsync(context, token, submitHandler));
            app.MapFallback(context => WriteJsonAsync(
                context,
                StatusCodes.Status404NotFound,
                LanCookieRelaySubmitResult.Failed("请求地址不存在")));

            var appStarted = false;
            try
            {
                await app.StartAsync(cancellationToken);
                appStarted = true;
                _terminalSubmissionStarted = 0;
                var session = BuildSession(app, sessionId, address, token);
                _app = app;
                _session = session;
                _timeoutCts = new CancellationTokenSource();
                _ = StopAfterTimeoutAsync(session.SessionId, _timeoutCts.Token);
                return session;
            }
            catch
            {
                await DisposeUnpublishedAppAsync(app, appStarted);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task StopAsync(
        LanCookieRelayStopReason reason = LanCookieRelayStopReason.Manual,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopCurrentSessionAsync(reason, null, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(LanCookieRelayStopReason.Manual);
        _gate.Dispose();
    }

    private async Task HandleSubmitAsync(
        HttpContext context,
        string token,
        Func<string, CancellationToken, Task<LanCookieRelaySubmitResult>> submitHandler)
    {
        if (!IsValidToken(context, token))
        {
            await WriteJsonAsync(
                context,
                StatusCodes.Status403Forbidden,
                LanCookieRelaySubmitResult.Failed("快传会话无效，请重新在电脑端启动局域网快传"));
            return;
        }

        if (Interlocked.CompareExchange(ref _terminalSubmissionStarted, 1, 0) != 0)
        {
            await TryWriteJsonAsync(
                context,
                StatusCodes.Status409Conflict,
                LanCookieRelaySubmitResult.Failed("快传会话已完成，请重新在电脑端启动局域网快传"));
            return;
        }

        try
        {
            LanCookieRelaySubmitResult result;
            try
            {
                var link = await SubmittedLinkReader.ReadLinkAsync(context);
                if (string.IsNullOrWhiteSpace(link))
                {
                    result = LanCookieRelaySubmitResult.Failed("没有收到授权链接，请先粘贴链接");
                }
                else
                {
                    result = await submitHandler(link, CancellationToken.None);
                }
            }
            catch (SubmittedLinkBodyTooLargeException)
            {
                await TryWriteJsonAsync(
                    context,
                    StatusCodes.Status413PayloadTooLarge,
                    LanCookieRelaySubmitResult.Failed("提交内容过大，请只粘贴授权链接"));
                return;
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "LAN cookie relay submission failed.");
                result = LanCookieRelaySubmitResult.Failed($"电脑端处理失败：{ex.Message}");
            }

            await TryWriteJsonAsync(
                context,
                result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest,
                result);
        }
        finally
        {
            _ = Task.Run(() => StopAsync(LanCookieRelayStopReason.Submitted));
        }
    }

    private static async Task WriteLandingPageAsync(HttpContext context, string token)
    {
        if (!IsValidToken(context, token))
        {
            await WriteJsonAsync(
                context,
                StatusCodes.Status403Forbidden,
                LanCookieRelaySubmitResult.Failed("快传会话无效，请重新在电脑端启动局域网快传"));
            return;
        }

        SetNoStore(context);
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(LanCookieRelayMobilePage.Build(token), context.RequestAborted);
    }

    private async Task StopAfterTimeoutAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(DefaultSessionTimeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await _gate.WaitAsync(CancellationToken.None);
        try
        {
            if (_session?.SessionId == sessionId)
            {
                await StopCurrentSessionAsync(
                    LanCookieRelayStopReason.Timeout,
                    "局域网快传已超时关闭",
                    CancellationToken.None);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task StopCurrentSessionAsync(
        LanCookieRelayStopReason reason,
        string? message,
        CancellationToken cancellationToken)
    {
        var app = _app;
        var timeoutCts = _timeoutCts;
        var session = _session;
        if (session is not null)
        {
            Interlocked.Exchange(ref _terminalSubmissionStarted, 1);
        }

        _app = null;
        _timeoutCts = null;
        _session = null;

        timeoutCts?.Cancel();
        timeoutCts?.Dispose();

        if (app is not null)
        {
            try
            {
                await app.StopAsync(cancellationToken);
            }
            finally
            {
                await app.DisposeAsync();
            }
        }

        if (session is not null)
        {
            Stopped?.Invoke(this, new LanCookieRelayStoppedEventArgs(session.SessionId, reason, message));
        }
    }

    private static LanCookieRelaySession BuildSession(
        WebApplication app,
        Guid sessionId,
        IPAddress address,
        string token)
    {
        var serverAddress = app.Urls.FirstOrDefault()
            ?? throw new InvalidOperationException("局域网快传服务启动成功，但未能读取监听地址");
        var builder = new UriBuilder(serverAddress)
        {
            Host = address.ToString(),
            Path = "/",
            Query = $"token={Uri.EscapeDataString(token)}"
        };

        return new LanCookieRelaySession(
            sessionId,
            builder.Uri,
            address.ToString(),
            builder.Port,
            DateTimeOffset.Now,
            DefaultSessionTimeout);
    }

    private static bool IsValidToken(HttpContext context, string expectedToken)
    {
        return string.Equals(
            context.Request.Query["token"].ToString(),
            expectedToken,
            StringComparison.Ordinal);
    }

    private static async Task WriteJsonAsync(
        HttpContext context,
        int statusCode,
        LanCookieRelaySubmitResult result)
    {
        SetNoStore(context);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            new { success = result.Success, message = result.Message },
            cancellationToken: context.RequestAborted);
    }

    private async Task TryWriteJsonAsync(
        HttpContext context,
        int statusCode,
        LanCookieRelaySubmitResult result)
    {
        try
        {
            await WriteJsonAsync(context, statusCode, result);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation("LAN cookie relay client disconnected before receiving the response.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to write LAN cookie relay response.");
        }
    }

    private async Task DisposeUnpublishedAppAsync(WebApplication app, bool appStarted)
    {
        try
        {
            if (appStarted)
            {
                await app.StopAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to stop unpublished LAN cookie relay app.");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    private static void SetNoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }

    private static string CreateToken()
    {
        return Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
    }

}
