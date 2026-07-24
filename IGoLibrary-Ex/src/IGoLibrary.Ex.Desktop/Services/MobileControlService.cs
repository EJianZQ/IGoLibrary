using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using IGoLibrary.Ex.Application.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class MobileControlService(
    ILanAddressProvider addressProvider,
    INetworkExposureManager networkExposureManager,
    IMobileControlStatusSnapshotProvider statusSnapshotProvider,
    IMobileControlTaskRecordsProvider taskRecordsProvider,
    IMobileControlTaskStartService taskStartService,
    IMobileControlActionService actionService,
    ILogger<MobileControlService> logger) : IMobileControlService, IAsyncDisposable
{
    private static readonly TimeSpan DeviceActiveWindow = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan DevicePruneInterval = TimeSpan.FromSeconds(10);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _devicesGate = new();
    private readonly NetworkRequestSecurityAuditor _securityAuditor = new(logger, "手机控制");
    private readonly Dictionary<string, DateTimeOffset> _deviceLastSeenByKey = new(StringComparer.Ordinal);
    private WebApplication? _app;
    private MobileControlSession? _session;
    private INetworkExposureLease? _exposureLease;
    private System.Threading.Timer? _devicePruneTimer;
    private int _connectedDeviceCount;

    public event EventHandler<MobileControlStoppedEventArgs>? Stopped;

    public event EventHandler<MobileControlDeviceCountChangedEventArgs>? DeviceCountChanged;

    public event EventHandler<MobileControlEndpointChangedEventArgs>? EndpointChanged;

    public MobileControlSession? CurrentSession => _session;

    public int ConnectedDeviceCount => Volatile.Read(ref _connectedDeviceCount);

    public async Task<MobileControlSession> StartAsync(
        MobileControlSettings settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (!MobileControlSettings.IsValidPort(settings.Port))
        {
            throw new InvalidOperationException("手机控制端口无效，请重新随机端口后再启动");
        }

        if (string.IsNullOrWhiteSpace(settings.AccessToken))
        {
            throw new InvalidOperationException("手机控制访问令牌无效，请重置访问令牌后再启动");
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            logger.LogInformation(
                "开始启动手机控制服务。请求模式={RequestedMode}，端口={Port}。",
                settings.NetworkMode,
                settings.Port);
            await StopCurrentSessionAsync(MobileControlStopReason.Replaced, null, cancellationToken);

            var address = addressProvider.GetPrimaryLanAddress()
                ?? throw new InvalidOperationException("没有找到可用的局域网 IPv4 地址，请确认电脑已连接到局域网");
            var sessionId = Guid.NewGuid();
            var token = settings.AccessToken.Trim();
            var healthCheckPath = $"/_igolibrary/health/{Guid.NewGuid():N}";

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ApplicationName = typeof(MobileControlService).Assembly.GetName().Name
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options =>
            {
                options.Listen(address, settings.Port);
            });

            var app = builder.Build();
            app.MapGet("/", context => WriteLandingPageAsync(context, token));
            app.MapGet(healthCheckPath, WriteHealthCheckAsync);
            app.MapGet("/api/status", context => WriteStatusAsync(context, token));
            app.MapGet("/api/task-records", context => WriteTaskRecordsAsync(context, token));
            app.MapGet("/api/session/auth-qrcode", context => WriteAuthQrCodeAsync(context, token));
            app.MapPost("/api/tasks/{kind}/start", context => WriteStartTaskAsync(context, token));
            app.MapPost("/api/tasks/{kind}/cancel", context => WriteCancelTaskAsync(context, token));
            app.MapPost("/api/reservation/cancel", context => WriteCancelReservationAsync(context, token));
            app.MapPost("/api/session/cookie/refresh", context => WriteRefreshCookieAsync(context, token));
            app.MapFallback(context => WriteJsonAsync(
                context,
                StatusCodes.Status404NotFound,
                new { success = false, message = "请求地址不存在" }));

            var appStarted = false;
            try
            {
                await app.StartAsync(cancellationToken);
                appStarted = true;
                var lanSession = BuildLanSession(sessionId, address, settings.Port, token);
                var exposureLease = await networkExposureManager.PublishAsync(
                    NetworkExposurePurpose.MobileControl,
                    lanSession.LanUrl,
                    healthCheckPath,
                    cancellationToken);
                var session = ApplyExposure(lanSession, exposureLease);
                _app = app;
                _session = session;
                _exposureLease = exposureLease;
                exposureLease.EndpointChanged += OnExposureEndpointChanged;
                _devicePruneTimer = new System.Threading.Timer(
                    _ => PruneConnectedDevices(),
                    null,
                    DevicePruneInterval,
                    DevicePruneInterval);
                logger.LogInformation(
                    "手机控制服务已启动。会话标识={SessionId}，生效模式={EffectiveMode}，端口={Port}。",
                    session.SessionId,
                    session.EffectiveMode,
                    session.Port);
                return session;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "手机控制服务启动失败。请求模式={RequestedMode}，端口={Port}。",
                    settings.NetworkMode,
                    settings.Port);
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
        MobileControlStopReason reason = MobileControlStopReason.Manual,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                await StopCurrentSessionAsync(reason, null, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "停止手机控制服务失败。原因={StopReason}。", reason);
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync(MobileControlStopReason.Manual);
        _gate.Dispose();
    }

    private async Task WriteLandingPageAsync(HttpContext context, string token)
    {
        if (!_securityAuditor.IsValidToken(context, token))
        {
            await WriteForbiddenAsync(context);
            return;
        }

        TrackConnectedDevice(context);
        SetNoStore(context);
        context.Response.ContentType = "text/html; charset=utf-8";
        await context.Response.WriteAsync(MobileControlMobilePage.Build(token), context.RequestAborted);
    }

    private async Task WriteStatusAsync(HttpContext context, string token)
    {
        if (!_securityAuditor.IsValidToken(context, token))
        {
            await WriteForbiddenAsync(context);
            return;
        }

        TrackConnectedDevice(context);
        SetNoStore(context);
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            await statusSnapshotProvider.CreateSnapshotAsync(context.RequestAborted),
            JsonOptions,
            context.RequestAborted);
    }

    private async Task WriteAuthQrCodeAsync(HttpContext context, string token)
    {
        if (!_securityAuditor.IsValidToken(context, token))
        {
            await WriteForbiddenAsync(context);
            return;
        }

        TrackConnectedDevice(context);
        SetNoStore(context);
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.ContentType = "image/png";
        var pngBytes = AuthQrCodeImageResource.GetPngBytes();
        context.Response.ContentLength = pngBytes.Length;
        await context.Response.Body.WriteAsync(pngBytes, context.RequestAborted);
    }

    private async Task WriteTaskRecordsAsync(HttpContext context, string token)
    {
        if (!_securityAuditor.IsValidToken(context, token))
        {
            await WriteForbiddenAsync(context);
            return;
        }

        TrackConnectedDevice(context);
        SetNoStore(context);
        MobileControlTaskRecordsSnapshot snapshot;
        try
        {
            snapshot = await taskRecordsProvider.CreateSnapshotAsync(context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "读取手机控制任务记录失败。");
            await WriteJsonAsync(
                context,
                StatusCodes.Status500InternalServerError,
                new { success = false, message = "读取任务记录失败，请稍后重试" });
            return;
        }

        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(
            context.Response.Body,
            snapshot,
            JsonOptions,
            context.RequestAborted);
    }

    private async Task WriteStartTaskAsync(HttpContext context, string token)
    {
        if (!_securityAuditor.IsValidToken(context, token))
        {
            await WriteForbiddenAsync(context);
            return;
        }

        TrackConnectedDevice(context);
        var taskKind = context.Request.RouteValues["kind"]?.ToString();
        if (string.IsNullOrWhiteSpace(taskKind))
        {
            await WriteActionResponseAsync(
                context,
                new MobileControlActionResult(false, "未知任务类型", StatusCodes.Status400BadRequest));
            return;
        }

        string? recordId = null;
        if (taskKind is "grab" or "globalLeak")
        {
            try
            {
                recordId = await MobileControlTaskStartRequestReader.ReadRecordIdAsync(context.Request);
            }
            catch (MobileControlTaskStartBodyTooLargeException)
            {
                LogRejectedPayload(context, "task_start_body_too_large", StatusCodes.Status413PayloadTooLarge);
                await WriteActionResponseAsync(
                    context,
                    new MobileControlActionResult(false, "提交内容过大", StatusCodes.Status413PayloadTooLarge));
                return;
            }
            catch (MobileControlTaskStartBodyException ex)
            {
                LogRejectedPayload(context, "task_start_body_invalid", StatusCodes.Status400BadRequest);
                await WriteActionResponseAsync(
                    context,
                    new MobileControlActionResult(false, ex.Message, StatusCodes.Status400BadRequest));
                return;
            }
            catch (JsonException)
            {
                LogRejectedPayload(context, "task_start_json_invalid", StatusCodes.Status400BadRequest);
                await WriteActionResponseAsync(
                    context,
                    new MobileControlActionResult(false, "提交内容格式无效", StatusCodes.Status400BadRequest));
                return;
            }
        }
        else if (taskKind == "occupy")
        {
            try
            {
                await MobileControlTaskStartRequestReader.EnsureEmptyBodyAsync(context.Request);
            }
            catch (MobileControlTaskStartBodyTooLargeException)
            {
                LogRejectedPayload(context, "occupy_body_too_large", StatusCodes.Status413PayloadTooLarge);
                await WriteActionResponseAsync(
                    context,
                    new MobileControlActionResult(false, "提交内容过大", StatusCodes.Status413PayloadTooLarge));
                return;
            }
            catch (MobileControlTaskStartBodyException ex)
            {
                LogRejectedPayload(context, "occupy_body_invalid", StatusCodes.Status400BadRequest);
                await WriteActionResponseAsync(
                    context,
                    new MobileControlActionResult(false, ex.Message, StatusCodes.Status400BadRequest));
                return;
            }
        }

        await WriteActionResultAsync(
            context,
            () => taskStartService.StartTaskAsync(taskKind, recordId, context.RequestAborted),
            "启动手机控制任务失败。");
    }

    private async Task WriteCancelTaskAsync(HttpContext context, string token)
    {
        if (!_securityAuditor.IsValidToken(context, token))
        {
            await WriteForbiddenAsync(context);
            return;
        }

        TrackConnectedDevice(context);
        var taskKind = context.Request.RouteValues["kind"]?.ToString();
        if (string.IsNullOrWhiteSpace(taskKind))
        {
            await WriteActionResponseAsync(
                context,
                new MobileControlActionResult(false, "未知任务类型", StatusCodes.Status400BadRequest));
            return;
        }

        await WriteActionResultAsync(
            context,
            () => actionService.CancelTaskAsync(taskKind, context.RequestAborted),
            "取消手机控制任务失败。");
    }

    private async Task WriteCancelReservationAsync(HttpContext context, string token)
    {
        if (!_securityAuditor.IsValidToken(context, token))
        {
            await WriteForbiddenAsync(context);
            return;
        }

        TrackConnectedDevice(context);
        await WriteActionResultAsync(
            context,
            () => actionService.CancelCurrentReservationAsync(context.RequestAborted),
            "取消手机控制预约失败。");
    }

    private async Task WriteRefreshCookieAsync(HttpContext context, string token)
    {
        if (!_securityAuditor.IsValidToken(context, token))
        {
            await WriteForbiddenAsync(context);
            return;
        }

        TrackConnectedDevice(context);
        string linkText;
        try
        {
            linkText = await SubmittedLinkReader.ReadLinkAsync(context);
        }
        catch (SubmittedLinkBodyTooLargeException)
        {
            LogRejectedPayload(context, "cookie_refresh_body_too_large", StatusCodes.Status413PayloadTooLarge);
            await WriteActionResponseAsync(
                context,
                new MobileControlActionResult(
                    false,
                    "提交内容过大，请只粘贴授权链接",
                    StatusCodes.Status413PayloadTooLarge));
            return;
        }
        catch (JsonException)
        {
            LogRejectedPayload(context, "cookie_refresh_json_invalid", StatusCodes.Status400BadRequest);
            await WriteActionResponseAsync(
                context,
                new MobileControlActionResult(
                    false,
                    "提交内容格式无效，请只提交授权链接",
                    StatusCodes.Status400BadRequest));
            return;
        }

        await WriteActionResultAsync(
            context,
            () => actionService.RefreshCookieFromLinkAsync(linkText, context.RequestAborted),
            "刷新手机控制 Cookie 失败。");
    }

    private async Task WriteActionResultAsync(
        HttpContext context,
        Func<Task<MobileControlActionResult>> action,
        string logMessage)
    {
        try
        {
            var result = await action();
            logger.LogInformation(
                "手机控制动作已处理。方法={Method}，路径={Path}，成功={Succeeded}，状态码={StatusCode}。",
                context.Request.Method,
                context.Request.Path.Value,
                result.Success,
                result.StatusCode);
            await WriteActionResponseAsync(context, result);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, logMessage);
            await WriteActionResponseAsync(
                context,
                new MobileControlActionResult(
                    false,
                    "操作失败，请稍后重试",
                    StatusCodes.Status500InternalServerError));
        }
    }

    private static Task WriteActionResponseAsync(
        HttpContext context,
        MobileControlActionResult result)
    {
        return WriteJsonAsync(
            context,
            result.StatusCode,
            new MobileControlActionResponse(result.Success, result.Message));
    }

    private async Task StopCurrentSessionAsync(
        MobileControlStopReason reason,
        string? message,
        CancellationToken cancellationToken)
    {
        var app = _app;
        var session = _session;
        var exposureLease = _exposureLease;
        var pruneTimer = _devicePruneTimer;
        _app = null;
        _session = null;
        _exposureLease = null;
        _devicePruneTimer = null;

        pruneTimer?.Dispose();
        ResetConnectedDevices();

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
            logger.LogInformation(
                "手机控制服务已停止。会话标识={SessionId}，原因={StopReason}。",
                session.SessionId,
                reason);
            PublishStopped(new MobileControlStoppedEventArgs(session.SessionId, reason, message));
        }
    }

    private void TrackConnectedDevice(HttpContext context)
    {
        var key = _session?.EffectiveMode == MobileControlNetworkMode.CloudflareTunnel &&
                  IPAddress.TryParse(context.Request.Headers["CF-Connecting-IP"].ToString(), out var connectingAddress)
            ? connectingAddress.ToString()
            : context.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrWhiteSpace(key))
        {
            key = context.Connection.Id;
        }

        var now = DateTimeOffset.UtcNow;
        int count;
        lock (_devicesGate)
        {
            _deviceLastSeenByKey[key] = now;
            RemoveStaleDevices(now);
            count = _deviceLastSeenByKey.Count;
        }

        PublishDeviceCount(count);
    }

    private void PruneConnectedDevices()
    {
        int count;
        lock (_devicesGate)
        {
            RemoveStaleDevices(DateTimeOffset.UtcNow);
            count = _deviceLastSeenByKey.Count;
        }

        PublishDeviceCount(count);
    }

    private void ResetConnectedDevices()
    {
        lock (_devicesGate)
        {
            _deviceLastSeenByKey.Clear();
        }

        PublishDeviceCount(0);
    }

    private void RemoveStaleDevices(DateTimeOffset now)
    {
        foreach (var pair in _deviceLastSeenByKey.ToArray())
        {
            if (now - pair.Value > DeviceActiveWindow)
            {
                _deviceLastSeenByKey.Remove(pair.Key);
            }
        }
    }

    private void PublishDeviceCount(int count)
    {
        if (Interlocked.Exchange(ref _connectedDeviceCount, count) == count)
        {
            return;
        }

        SafeEventPublisher.Publish(
            this,
            DeviceCountChanged,
            new MobileControlDeviceCountChangedEventArgs(count),
            logger,
            "手机控制设备数量事件订阅者处理失败。");
    }

    private static MobileControlSession BuildLanSession(
        Guid sessionId,
        IPAddress address,
        int port,
        string token)
    {
        var builder = new UriBuilder("http", address.ToString(), port)
        {
            Path = "/",
            Query = $"token={Uri.EscapeDataString(token)}"
        };

        var lanUrl = builder.Uri;
        return new MobileControlSession(
            sessionId,
            lanUrl,
            lanUrl,
            address.ToString(),
            port,
            DateTimeOffset.Now,
            MobileControlNetworkMode.LocalNetwork);
    }

    private static MobileControlSession ApplyExposure(
        MobileControlSession session,
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
        logger.LogInformation(
            "手机控制发布端点已更新。会话标识={SessionId}，生效模式={EffectiveMode}。",
            session.SessionId,
            session.EffectiveMode);
        PublishEndpointChanged(new MobileControlEndpointChangedEventArgs(session));
    }

    private static Task WriteHealthCheckAsync(HttpContext context)
    {
        SetNoStore(context);
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return Task.CompletedTask;
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
            logger.LogWarning(ex, "停止尚未发布的手机控制应用失败。");
        }
        finally
        {
            await app.DisposeAsync();
        }
    }

    private static Task WriteForbiddenAsync(HttpContext context)
    {
        return WriteJsonAsync(
            context,
            StatusCodes.Status403Forbidden,
            new { success = false, message = "手机控制访问令牌无效，请从电脑端重新扫码进入" });
    }

    private void LogRejectedPayload(HttpContext context, string reason, int statusCode)
    {
        logger.LogWarning(
            "手机控制拒绝无效请求内容。方法={Method}，路径={Path}，原因={Reason}，状态码={StatusCode}，内容长度={ContentLength}。",
            context.Request.Method,
            context.Request.Path.Value,
            reason,
            statusCode,
            context.Request.ContentLength);
    }

    private void PublishStopped(MobileControlStoppedEventArgs args)
    {
        SafeEventPublisher.Publish(
            this,
            Stopped,
            args,
            logger,
            "手机控制停止事件订阅者处理失败。");
    }

    private void PublishEndpointChanged(MobileControlEndpointChangedEventArgs args)
    {
        SafeEventPublisher.Publish(
            this,
            EndpointChanged,
            args,
            logger,
            "手机控制端点变更事件订阅者处理失败。");
    }

    private static async Task WriteJsonAsync(
        HttpContext context,
        int statusCode,
        object value)
    {
        SetNoStore(context);
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/json; charset=utf-8";
        await JsonSerializer.SerializeAsync(context.Response.Body, value, JsonOptions, context.RequestAborted);
    }

    private static void SetNoStore(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        context.Response.Headers.Expires = "0";
    }
}
