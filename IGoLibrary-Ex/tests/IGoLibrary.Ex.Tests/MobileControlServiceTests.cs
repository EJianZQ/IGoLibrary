using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using IGoLibrary.Ex.Desktop.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class MobileControlServiceTests
{
    [Fact]
    public async Task StartAndReadEndpoints_ReturnExpectedContentAndSessionState()
    {
        await StartAsync_ListensOnConfiguredAddressAndPort();
        await GetRoot_WithValidToken_ReturnsMobileHtmlWithNoStore();
        await GetRoot_WithValidToken_TracksConnectedDeviceCount();
        await GetStatus_WithValidToken_ReturnsSnapshotJson();
        await GetAuthQrCode_WithValidToken_ReturnsPngWithNoStore();
    }

    private async Task StartAsync_ListensOnConfiguredAddressAndPort()
    {
        var port = GetFreeTcpPort();
        await using var service = CreateService();

        var session = await service.StartAsync(new MobileControlSettings(port, "token"));

        Assert.Equal(IPAddress.Loopback.ToString(), session.Host);
        Assert.Equal(port, session.Port);
        Assert.Equal($"http://127.0.0.1:{port}/?token=token", session.Url.ToString());
    }

    private async Task GetRoot_WithValidToken_ReturnsMobileHtmlWithNoStore()
    {
        var port = GetFreeTcpPort();
        await using var service = CreateService();
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();

        using var response = await client.GetAsync(session.Url);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("手机控制", html);
        Assert.Contains("/api/status", html);
    }

    private async Task GetRoot_WithValidToken_TracksConnectedDeviceCount()
    {
        var port = GetFreeTcpPort();
        await using var service = CreateService();
        var observedCounts = new List<int>();
        service.DeviceCountChanged += (_, args) => observedCounts.Add(args.ConnectedDeviceCount);
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();

        using var response = await client.GetAsync(session.Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, service.ConnectedDeviceCount);
        Assert.Contains(1, observedCounts);

        await service.StopAsync();

        Assert.Equal(0, service.ConnectedDeviceCount);
        Assert.Contains(0, observedCounts);
    }

    private async Task GetStatus_WithValidToken_ReturnsSnapshotJson()
    {
        var provider = new FakeMobileControlStatusSnapshotProvider();
        var port = GetFreeTcpPort();
        await using var service = CreateService(provider);
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();

        using var response = await client.GetAsync(new Uri(session.Url, "/api/status?token=token"));
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal(1, provider.CreateSnapshotCalls);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("已登录", document.RootElement.GetProperty("cookie").GetProperty("statusText").GetString());
        Assert.Equal("运行中", document.RootElement.GetProperty("grab").GetProperty("stateText").GetString());
    }

    private async Task GetAuthQrCode_WithValidToken_ReturnsPngWithNoStore()
    {
        var port = GetFreeTcpPort();
        await using var service = CreateService();
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();

        using var response = await client.GetAsync(new Uri(session.Url, "/api/session/auth-qrcode?token=token"));
        var bytes = await response.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.True(bytes.Length > 8);
        Assert.Equal(0x89, bytes[0]);
        Assert.Equal((byte)'P', bytes[1]);
        Assert.Equal((byte)'N', bytes[2]);
        Assert.Equal((byte)'G', bytes[3]);
    }

    [Fact]
    public async Task TaskRecordAndStartEndpoints_EnforceContractsAndAuthentication()
    {
        var recordId = Guid.NewGuid().ToString("N");
        var recordsProvider = new FakeMobileControlTaskRecordsProvider
        {
            Snapshot = new MobileControlTaskRecordsSnapshot(
                DateTimeOffset.UtcNow,
                [new MobileControlGrabTaskRecordSnapshot(
                    recordId,
                    "2026-07-16 12:00:00",
                    "电子阅览室A",
                    ["27", "38"],
                    "随机延迟",
                    "直接发送预约请求")],
                [])
        };
        var startService = new FakeMobileControlTaskStartService();
        var port = GetFreeTcpPort();
        await using var service = CreateService(
            taskRecordsProvider: recordsProvider,
            taskStartService: startService);
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();

        using var recordsResponse = await client.GetAsync(new Uri(session.Url, "/api/task-records?token=token"));
        Assert.Equal(HttpStatusCode.OK, recordsResponse.StatusCode);
        Assert.True(recordsResponse.Headers.CacheControl?.NoStore);
        using var recordsJson = JsonDocument.Parse(await recordsResponse.Content.ReadAsStringAsync());
        Assert.Equal(recordId, recordsJson.RootElement.GetProperty("grab")[0].GetProperty("recordId").GetString());

        using var grabContent = new StringContent(
            JsonSerializer.Serialize(new { recordId }),
            System.Text.Encoding.UTF8,
            "application/json");
        using var grabResponse = await client.PostAsync(
            new Uri(session.Url, "/api/tasks/grab/start?token=token"),
            grabContent);
        using var occupyResponse = await client.PostAsync(
            new Uri(session.Url, "/api/tasks/occupy/start?token=token"),
            null);
        Assert.Equal(HttpStatusCode.OK, grabResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, occupyResponse.StatusCode);
        Assert.True(grabResponse.Headers.CacheControl?.NoStore);
        Assert.True(occupyResponse.Headers.CacheControl?.NoStore);
        Assert.Equal([("grab", recordId), ("occupy", null)], startService.Starts);

        using var invalidRecordsResponse = await client.GetAsync(
            new Uri(session.Url, "/api/task-records?token=bad"));
        using var invalidTokenResponse = await client.PostAsync(
            new Uri(session.Url, "/api/tasks/grab/start?token=bad"),
            new StringContent("not-json"));
        using var invalidContentResponse = await client.PostAsync(
            new Uri(session.Url, "/api/tasks/grab/start?token=token"),
            new StringContent(recordId));
        using var oversizedResponse = await client.PostAsync(
            new Uri(session.Url, "/api/tasks/grab/start?token=token"),
            new StringContent(
                JsonSerializer.Serialize(new { recordId = new string('a', 1100) }),
                System.Text.Encoding.UTF8,
                "application/json"));
        using var unexpectedJsonResponse = await client.PostAsync(
            new Uri(session.Url, "/api/tasks/grab/start?token=token"),
            new StringContent(
                JsonSerializer.Serialize(new { recordId, unexpected = true }),
                System.Text.Encoding.UTF8,
                "application/json"));
        using var occupyBodyResponse = await client.PostAsync(
            new Uri(session.Url, "/api/tasks/occupy/start?token=token"),
            new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));
        using var oversizedOccupyResponse = await client.PostAsync(
            new Uri(session.Url, "/api/tasks/occupy/start?token=token"),
            new StringContent(new string('a', 1025)));
        Assert.Equal(HttpStatusCode.Forbidden, invalidRecordsResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, invalidTokenResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalidContentResponse.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unexpectedJsonResponse.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, occupyBodyResponse.StatusCode);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, oversizedOccupyResponse.StatusCode);
        Assert.Equal(1, recordsProvider.Calls);
        Assert.Equal(2, startService.Starts.Count);
    }

    [Fact]
    public async Task TaskRecords_WhenProviderFails_ReturnsControlledErrorAndLogsException()
    {
        var expected = new InvalidOperationException("database path must not reach the client");
        var recordsProvider = new FakeMobileControlTaskRecordsProvider { Error = expected };
        var logger = new CapturingLogger<MobileControlService>();
        var port = GetFreeTcpPort();
        await using var service = CreateService(taskRecordsProvider: recordsProvider, logger: logger);
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();

        using var response = await client.GetAsync(new Uri(session.Url, "/api/task-records?token=token"));
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("读取任务记录失败，请稍后重试", document.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("database path", document.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && ReferenceEquals(entry.Exception, expected));
    }

    [Fact]
    public async Task StartTask_WhenServiceFails_ReturnsGenericInternalErrorAndLogsException()
    {
        var expected = new InvalidOperationException("internal path must not reach the client");
        var startService = new FakeMobileControlTaskStartService { Error = expected };
        var logger = new CapturingLogger<MobileControlService>();
        var port = GetFreeTcpPort();
        await using var service = CreateService(taskStartService: startService, logger: logger);
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();

        using var response = await client.PostAsync(
            new Uri(session.Url, "/api/tasks/occupy/start?token=token"),
            null);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Equal("操作失败，请稍后重试", document.RootElement.GetProperty("message").GetString());
        Assert.DoesNotContain("internal path", document.RootElement.GetRawText(), StringComparison.Ordinal);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning && ReferenceEquals(entry.Exception, expected));
    }

    [Fact]
    public async Task AllProtectedEndpoints_RejectInvalidAuthentication()
    {
        await GetAuthQrCode_WithInvalidToken_IsRejected();
        await Requests_WithInvalidToken_AreRejected();
        await PostCancelTask_WithInvalidToken_IsRejected();
        await PostCancelReservation_WithInvalidToken_IsRejected();
        await PostRefreshCookie_WithInvalidToken_IsRejected();
    }

    private async Task GetAuthQrCode_WithInvalidToken_IsRejected()
    {
        var port = GetFreeTcpPort();
        await using var service = CreateService();
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();

        using var response = await client.GetAsync(new Uri(session.Url, "/api/session/auth-qrcode?token=bad"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private async Task Requests_WithInvalidToken_AreRejected()
    {
        var provider = new FakeMobileControlStatusSnapshotProvider();
        var port = GetFreeTcpPort();
        await using var service = CreateService(provider);
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();

        using var response = await client.GetAsync(new Uri(session.Url, "/api/status?token=bad"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, provider.CreateSnapshotCalls);
    }

    [Fact]
    public async Task PostCancelTask_MapsSuccessAndConflictResults()
    {
        await PostCancelTask_WithValidToken_ReturnsActionJson();
        await PostCancelTask_WhenActionConflicts_ReturnsConflict();
    }

    private async Task PostCancelTask_WithValidToken_ReturnsActionJson()
    {
        var actionService = new FakeMobileControlActionService();
        var port = GetFreeTcpPort();
        await using var service = CreateService(actionService: actionService);
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();

        using var response = await client.PostAsync(new Uri(session.Url, "/api/tasks/grab/cancel?token=token"), null);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(["grab"], actionService.CancelledTaskKinds);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("已取消任务", document.RootElement.GetProperty("message").GetString());
    }

    private async Task PostCancelTask_WithInvalidToken_IsRejected()
    {
        var actionService = new FakeMobileControlActionService();
        var port = GetFreeTcpPort();
        await using var service = CreateService(actionService: actionService);
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();

        using var response = await client.PostAsync(new Uri(session.Url, "/api/tasks/grab/cancel?token=bad"), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(actionService.CancelledTaskKinds);
    }

    private async Task PostCancelTask_WhenActionConflicts_ReturnsConflict()
    {
        var actionService = new FakeMobileControlActionService
        {
            CancelTaskResult = new MobileControlActionResult(false, "抢座任务当前未运行", StatusCodes.Status409Conflict)
        };
        var port = GetFreeTcpPort();
        await using var service = CreateService(actionService: actionService);
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();

        using var response = await client.PostAsync(new Uri(session.Url, "/api/tasks/grab/cancel?token=token"), null);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("抢座任务当前未运行", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PostCancelReservation_WithValidToken_CallsActionService()
    {
        var actionService = new FakeMobileControlActionService();
        var port = GetFreeTcpPort();
        await using var service = CreateService(actionService: actionService);
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();

        using var response = await client.PostAsync(new Uri(session.Url, "/api/reservation/cancel?token=token"), null);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, actionService.CancelReservationCalls);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("已取消预约", document.RootElement.GetProperty("message").GetString());
    }

    private async Task PostCancelReservation_WithInvalidToken_IsRejected()
    {
        var actionService = new FakeMobileControlActionService();
        var port = GetFreeTcpPort();
        await using var service = CreateService(actionService: actionService);
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();

        using var response = await client.PostAsync(new Uri(session.Url, "/api/reservation/cancel?token=bad"), null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, actionService.CancelReservationCalls);
    }

    [Fact]
    public async Task PostRefreshCookie_AcceptsEverySupportedRequestBody()
    {
        (string ContentType, string Body)[] cases =
        [
            ("application/json", "{\"link\":\"https://example.test/auth?code=1234567890abcdef1234567890abcdef\"}"),
            ("application/x-www-form-urlencoded", "link=https%3A%2F%2Fexample.test%2Fauth%3Fcode%3D1234567890abcdef1234567890abcdef"),
            ("text/plain", "https://example.test/auth?code=1234567890abcdef1234567890abcdef")
        ];

        foreach (var (contentType, body) in cases)
        {
            await PostRefreshCookie_WithValidToken_ReadsSubmittedLinkAndReturnsActionJson(contentType, body);
        }
    }

    private async Task PostRefreshCookie_WithValidToken_ReadsSubmittedLinkAndReturnsActionJson(
        string contentType,
        string body)
    {
        var actionService = new FakeMobileControlActionService();
        var port = GetFreeTcpPort();
        await using var service = CreateService(actionService: actionService);
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();
        using var content = new StringContent(body);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        using var response = await client.PostAsync(
            new Uri(session.Url, "/api/session/cookie/refresh?token=token"),
            content);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            ["https://example.test/auth?code=1234567890abcdef1234567890abcdef"],
            actionService.RefreshedCookieLinks);
        using var document = JsonDocument.Parse(json);
        Assert.True(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Cookie 已刷新", document.RootElement.GetProperty("message").GetString());
    }

    private async Task PostRefreshCookie_WithInvalidToken_IsRejected()
    {
        var actionService = new FakeMobileControlActionService();
        var port = GetFreeTcpPort();
        await using var service = CreateService(actionService: actionService);
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();
        using var content = new StringContent("https://example.test/auth?code=1234567890abcdef1234567890abcdef");

        using var response = await client.PostAsync(
            new Uri(session.Url, "/api/session/cookie/refresh?token=bad"),
            content);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(actionService.RefreshedCookieLinks);
    }

    [Fact]
    public async Task PostRefreshCookie_WhenActionFails_ReturnsActionStatusCode()
    {
        var actionService = new FakeMobileControlActionService
        {
            RefreshCookieResult = new MobileControlActionResult(
                false,
                "Cookie 已获取，但自动验证失败：invalid cookie",
                StatusCodes.Status422UnprocessableEntity)
        };
        var port = GetFreeTcpPort();
        await using var service = CreateService(actionService: actionService);
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();
        using var content = new StringContent("https://example.test/auth?code=1234567890abcdef1234567890abcdef");

        using var response = await client.PostAsync(
            new Uri(session.Url, "/api/session/cookie/refresh?token=token"),
            content);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("Cookie 已获取，但自动验证失败：invalid cookie", document.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task PostRefreshCookie_WhenBodyIsTooLarge_ReturnsPayloadTooLargeWithoutCallingAction()
    {
        var actionService = new FakeMobileControlActionService();
        var port = GetFreeTcpPort();
        await using var service = CreateService(actionService: actionService);
        var session = await service.StartAsync(new MobileControlSettings(port, "token"));
        using var client = new HttpClient();
        using var content = new StringContent(new string('x', (int)SubmittedLinkReader.MaxRequestBodyBytes + 1));

        using var response = await client.PostAsync(
            new Uri(session.Url, "/api/session/cookie/refresh?token=token"),
            content);
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty(actionService.RefreshedCookieLinks);
        using var document = JsonDocument.Parse(json);
        Assert.False(document.RootElement.GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task CloudflareMode_UsesPublicEndpointAndSupportsLiveSwitching()
    {
        await StartAsync_InCloudflareMode_ReturnsPublicUrlAndKeepsLanUrl();
        await ActiveSession_LiveModeSwitchPreservesKestrelSessionAndPort();
    }

    private async Task StartAsync_InCloudflareMode_ReturnsPublicUrlAndKeepsLanUrl()
    {
        var exposureManager = new FakeNetworkExposureManager();
        exposureManager.Initialize(MobileControlNetworkMode.CloudflareTunnel, CloudflareTunnelProxyMode.Auto, string.Empty);
        await using var service = CreateService(exposureManager: exposureManager);
        var port = GetFreeTcpPort();

        var session = await service.StartAsync(new MobileControlSettings(port, "token"));

        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, session.EffectiveMode);
        Assert.Equal("https://unit-test.trycloudflare.com/?token=token", session.Url.ToString());
        Assert.Equal($"http://127.0.0.1:{port}/?token=token", session.LanUrl.ToString());
    }

    private async Task ActiveSession_LiveModeSwitchPreservesKestrelSessionAndPort()
    {
        var exposureManager = new FakeNetworkExposureManager();
        await using var service = CreateService(exposureManager: exposureManager);
        var original = await service.StartAsync(new MobileControlSettings(GetFreeTcpPort(), "token"));
        MobileControlSession? changed = null;
        service.EndpointChanged += (_, e) => changed = e.Session;

        await exposureManager.SetModeAsync(MobileControlNetworkMode.CloudflareTunnel);

        var updated = Assert.IsType<MobileControlSession>(changed);
        Assert.Equal(original.SessionId, updated.SessionId);
        Assert.Equal(original.Port, updated.Port);
        Assert.Equal(original.LanUrl, updated.LanUrl);
        Assert.Equal("https", updated.Url.Scheme);
        using var response = await new HttpClient().GetAsync(original.LanUrl);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static MobileControlService CreateService(
        IMobileControlStatusSnapshotProvider? statusSnapshotProvider = null,
        IMobileControlActionService? actionService = null,
        INetworkExposureManager? exposureManager = null,
        IMobileControlTaskRecordsProvider? taskRecordsProvider = null,
        IMobileControlTaskStartService? taskStartService = null,
        ILogger<MobileControlService>? logger = null)
    {
        return new MobileControlService(
            new FixedLanAddressProvider(IPAddress.Loopback),
            exposureManager ?? new FakeNetworkExposureManager(),
            statusSnapshotProvider ?? new FakeMobileControlStatusSnapshotProvider(),
            taskRecordsProvider ?? new FakeMobileControlTaskRecordsProvider(),
            taskStartService ?? new FakeMobileControlTaskStartService(),
            actionService ?? new FakeMobileControlActionService(),
            logger ?? NullLogger<MobileControlService>.Instance);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class FixedLanAddressProvider(IPAddress address) : ILanAddressProvider
    {
        public IPAddress? GetPrimaryLanAddress()
        {
            return address;
        }
    }

    private sealed class FakeMobileControlStatusSnapshotProvider : IMobileControlStatusSnapshotProvider
    {
        public int CreateSnapshotCalls { get; private set; }

        public Task<MobileControlStatusSnapshot> CreateSnapshotAsync(CancellationToken cancellationToken = default)
        {
            CreateSnapshotCalls++;
            return Task.FromResult(new MobileControlStatusSnapshot(
                new DateTimeOffset(2026, 7, 6, 12, 0, 0, TimeSpan.Zero),
                "2026-07-06 12:00:00",
                new MobileControlCookieSnapshot(true, "已登录", "授权链接", "2026-07-06 11:00:00", "2026-07-06 13:00:00", "01:00:00", 50, "normal"),
                new MobileControlReservationSnapshot(true, "自科 / 1", "自科", "1", "2026-07-06 13:00:00", "01:00:00", 50, "normal"),
                new MobileControlGrabTaskSnapshot("运行中", "抢座任务已启动", true, 3, 2, "刚刚", "00:00:05", ["[12:00:00] Grab: test"]),
                new MobileControlGlobalLeakTaskSnapshot("未运行", "未运行", false, 0, 0, "无", "00:00:00", []),
                new MobileControlTomorrowTaskSnapshot("未运行", "未运行", false, "20:00:00", 0, "无", "尚未执行明日预约", "00:00:00", []),
                new MobileControlOccupyTaskSnapshot("未运行", "未运行", false, "无", "等待建立预约状态", [])));
        }
    }

    private sealed class FakeMobileControlActionService : IMobileControlActionService
    {
        public List<string> CancelledTaskKinds { get; } = [];

        public List<string> RefreshedCookieLinks { get; } = [];

        public int CancelReservationCalls { get; private set; }

        public MobileControlActionResult CancelTaskResult { get; set; } =
            new(true, "已取消任务", StatusCodes.Status200OK);

        public MobileControlActionResult CancelReservationResult { get; set; } =
            new(true, "已取消预约", StatusCodes.Status200OK);

        public MobileControlActionResult RefreshCookieResult { get; set; } =
            new(true, "Cookie 已刷新", StatusCodes.Status200OK);

        public Task<MobileControlActionResult> CancelTaskAsync(
            string taskKind,
            CancellationToken cancellationToken = default)
        {
            CancelledTaskKinds.Add(taskKind);
            return Task.FromResult(CancelTaskResult);
        }

        public Task<MobileControlActionResult> CancelCurrentReservationAsync(
            CancellationToken cancellationToken = default)
        {
            CancelReservationCalls++;
            return Task.FromResult(CancelReservationResult);
        }

        public Task<MobileControlActionResult> RefreshCookieFromLinkAsync(
            string linkText,
            CancellationToken cancellationToken = default)
        {
            RefreshedCookieLinks.Add(linkText);
            return Task.FromResult(RefreshCookieResult);
        }
    }

    private sealed class FakeMobileControlTaskRecordsProvider : IMobileControlTaskRecordsProvider
    {
        public int Calls { get; private set; }

        public MobileControlTaskRecordsSnapshot Snapshot { get; set; } =
            new(DateTimeOffset.UtcNow, [], []);

        public Exception? Error { get; init; }

        public Task<MobileControlTaskRecordsSnapshot> CreateSnapshotAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (Error is not null)
            {
                return Task.FromException<MobileControlTaskRecordsSnapshot>(Error);
            }

            return Task.FromResult(Snapshot);
        }
    }

    private sealed class FakeMobileControlTaskStartService : IMobileControlTaskStartService
    {
        public List<(string TaskKind, string? RecordId)> Starts { get; } = [];

        public MobileControlActionResult Result { get; set; } = new(
            true,
            "任务启动请求已被接受",
            StatusCodes.Status200OK);

        public Exception? Error { get; init; }

        public Task<MobileControlActionResult> StartTaskAsync(
            string taskKind,
            string? recordId,
            CancellationToken cancellationToken = default)
        {
            Starts.Add((taskKind, recordId));
            if (Error is not null)
            {
                return Task.FromException<MobileControlActionResult>(Error);
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, Exception? Exception)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, exception));
        }
    }
}
