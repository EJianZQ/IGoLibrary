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
    INetworkExposureManager networkExposureManager,
    ILogger<LanCookieRelayService> logger) : ILanCookieRelayService, IAsyncDisposable
{
    private static readonly TimeSpan DefaultSessionTimeout = TimeSpan.FromMinutes(10);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WebApplication? _app;
    private CancellationTokenSource? _timeoutCts;
    private LanCookieRelaySession? _session;
    private SubmitGate? _submitGate;
    private INetworkExposureLease? _exposureLease;

    public event EventHandler<LanCookieRelayStoppedEventArgs>? Stopped;

    public event EventHandler<LanCookieRelayEndpointChangedEventArgs>? EndpointChanged;

    public async Task<LanCookieRelaySession> StartAsync(
        Func<string, CancellationToken, Task<LanCookieRelaySubmitResult>> submitHandler,
        CancellationToken cancellationToken = default)
    {
        return await StartAsync(submitHandler, LanAuthLinkRelayPurpose.GraphQlSession, cancellationToken);
    }

    public async Task<LanCookieRelaySession> StartAsync(
        Func<string, CancellationToken, Task<LanCookieRelaySubmitResult>> submitHandler,
        LanAuthLinkRelayPurpose purpose,
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
            var healthCheckPath = $"/_igolibrary/health/{CreateToken()}";

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

            var submitGate = new SubmitGate();
            var app = builder.Build();
            app.MapGet("/", context => WriteLandingPageAsync(context, token, purpose));
            app.MapGet("/auth-qrcode", context => WriteAuthQrCodeAsync(context, token));
            app.MapGet(healthCheckPath, WriteHealthCheckAsync);
            app.MapPost("/submit", context => HandleSubmitAsync(context, token, submitHandler, submitGate));
            app.MapFallback(context => WriteJsonAsync(
                context,
                StatusCodes.Status404NotFound,
                LanCookieRelaySubmitResult.Failed("请求地址不存在")));

            var appStarted = false;
            try
            {
                await app.StartAsync(cancellationToken);
                appStarted = true;
                var lanSession = BuildLanSession(app, sessionId, address, token);
                var exposureLease = await networkExposureManager.PublishAsync(
                    NetworkExposurePurpose.AuthorizationRelay,
                    lanSession.LanUrl,
                    healthCheckPath,
                    cancellationToken);
                var session = ApplyExposure(lanSession, exposureLease);
                _app = app;
                _session = session;
                _submitGate = submitGate;
                _exposureLease = exposureLease;
                exposureLease.EndpointChanged += OnExposureEndpointChanged;
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
        Func<string, CancellationToken, Task<LanCookieRelaySubmitResult>> submitHandler,
        SubmitGate submitGate)
    {
        if (!IsValidToken(context, token))
        {
            await WriteJsonAsync(
                context,
                StatusCodes.Status403Forbidden,
                LanCookieRelaySubmitResult.Failed("快传会话无效，请重新在电脑端启动快传"));
            return;
        }

        var beginResult = submitGate.TryBegin();
        if (beginResult == SubmitBeginResult.Completed)
        {
            await TryWriteJsonAsync(
                context,
                StatusCodes.Status409Conflict,
                LanCookieRelaySubmitResult.Failed("快传会话已完成，请重新在电脑端启动快传"));
            return;
        }

        if (beginResult == SubmitBeginResult.InProgress)
        {
            await TryWriteJsonAsync(
                context,
                StatusCodes.Status409Conflict,
                LanCookieRelaySubmitResult.Failed("已有提交正在电脑端处理，请稍后再试"));
            return;
        }

        var shouldStopSession = false;
        try
        {
            LanCookieRelaySubmitResult result;
            var statusCode = StatusCodes.Status400BadRequest;
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

                statusCode = result.Success
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status400BadRequest;
            }
            catch (SubmittedLinkBodyTooLargeException)
            {
                statusCode = StatusCodes.Status413PayloadTooLarge;
                result = LanCookieRelaySubmitResult.Failed("提交内容过大，请只粘贴授权链接");
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "局域网 Cookie 中继提交失败。");
                result = LanCookieRelaySubmitResult.Failed($"电脑端处理失败：{ex.Message}");
            }

            shouldStopSession = result.Success;
            await TryWriteJsonAsync(context, statusCode, result);
        }
        finally
        {
            if (shouldStopSession)
            {
                submitGate.Complete();
                _ = Task.Run(() => StopAsync(LanCookieRelayStopReason.Submitted));
            }
            else
            {
                submitGate.Release();
            }
        }
    }

    private async Task WriteLandingPageAsync(
        HttpContext context,
        string token,
        LanAuthLinkRelayPurpose purpose)
    {
        if (!IsValidToken(context, token))
        {
            await WriteJsonAsync(
                context,
                StatusCodes.Status403Forbidden,
                LanCookieRelaySubmitResult.Failed("快传会话无效，请重新在电脑端启动快传"));
            return;
        }

        SetNoStore(context);
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(
            LanCookieRelayMobilePage.Build(
                token,
                purpose,
                _session?.EffectiveMode ?? IGoLibrary.Ex.Application.Configuration.MobileControlNetworkMode.LocalNetwork),
            context.RequestAborted);
    }

    private static async Task WriteAuthQrCodeAsync(HttpContext context, string token)
    {
        if (!IsValidToken(context, token))
        {
            await WriteJsonAsync(
                context,
                StatusCodes.Status403Forbidden,
                LanCookieRelaySubmitResult.Failed("快传会话无效，请重新在电脑端启动快传"));
            return;
        }

        SetNoStore(context);
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.ContentType = "image/png";
        var pngBytes = AuthQrCodeImageResource.GetPngBytes();
        context.Response.ContentLength = pngBytes.Length;
        await context.Response.Body.WriteAsync(pngBytes, context.RequestAborted);
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
                    "快传已超时关闭",
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
        var submitGate = _submitGate;
        var exposureLease = _exposureLease;

        _app = null;
        _timeoutCts = null;
        _session = null;
        _submitGate = null;
        _exposureLease = null;

        submitGate?.Complete();

        timeoutCts?.Cancel();
        timeoutCts?.Dispose();

        if (exposureLease is not null)
        {
            exposureLease.EndpointChanged -= OnExposureEndpointChanged;
            await exposureLease.DisposeAsync();
        }

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

    private static LanCookieRelaySession BuildLanSession(
        WebApplication app,
        Guid sessionId,
        IPAddress address,
        string token)
    {
        var serverAddress = app.Urls.FirstOrDefault()
            ?? throw new InvalidOperationException("快传本机服务启动成功，但未能读取监听地址");
        var builder = new UriBuilder(serverAddress)
        {
            Host = address.ToString(),
            Path = "/",
            Query = $"token={Uri.EscapeDataString(token)}"
        };

        var lanUrl = builder.Uri;
        return new LanCookieRelaySession(
            sessionId,
            lanUrl,
            lanUrl,
            address.ToString(),
            builder.Port,
            DateTimeOffset.Now,
            DefaultSessionTimeout,
            IGoLibrary.Ex.Application.Configuration.MobileControlNetworkMode.LocalNetwork);
    }

    private static LanCookieRelaySession ApplyExposure(
        LanCookieRelaySession session,
        INetworkExposureLease exposureLease)
    {
        return session with
        {
            Url = exposureLease.Url,
            EffectiveMode = exposureLease.EffectiveMode
        };
    }

    private void OnExposureEndpointChanged(object? sender, NetworkExposureChangedEventArgs e)
    {
        var session = _session;
        if (session is null || sender is not INetworkExposureLease lease || lease.Id != _exposureLease?.Id)
        {
            return;
        }

        session = session with
        {
            Url = e.Url,
            EffectiveMode = e.EffectiveMode
        };
        _session = session;
        EndpointChanged?.Invoke(this, new LanCookieRelayEndpointChangedEventArgs(session));
    }

    private static Task WriteHealthCheckAsync(HttpContext context)
    {
        SetNoStore(context);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return Task.CompletedTask;
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
            logger.LogInformation("局域网 Cookie 中继客户端在收到响应前已断开连接。");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "写入局域网 Cookie 中继响应失败。");
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
            logger.LogWarning(ex, "停止尚未发布的局域网 Cookie 中继应用失败。");
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

    private enum SubmitBeginResult
    {
        Started,
        InProgress,
        Completed
    }

    private sealed class SubmitGate
    {
        private int _submissionInProgress;
        private int _completed;

        public SubmitBeginResult TryBegin()
        {
            if (Volatile.Read(ref _completed) != 0)
            {
                return SubmitBeginResult.Completed;
            }

            if (Interlocked.CompareExchange(ref _submissionInProgress, 1, 0) != 0)
            {
                return SubmitBeginResult.InProgress;
            }

            if (Volatile.Read(ref _completed) == 0)
            {
                return SubmitBeginResult.Started;
            }

            Release();
            return SubmitBeginResult.Completed;
        }

        public void Release()
        {
            Interlocked.Exchange(ref _submissionInProgress, 0);
        }

        public void Complete()
        {
            Interlocked.Exchange(ref _completed, 1);
            Interlocked.Exchange(ref _submissionInProgress, 1);
        }
    }

}
