using System.Net;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using IGoLibrary.Ex.Desktop.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class LanCookieRelayServiceTests
{
    [Fact]
    public async Task StartAndReadEndpoints_ReturnExpectedContent()
    {
        await StartAsync_ListensOnProvidedAddressAndRandomPort();
        await GetRoot_ReturnsMobileHtmlWithNoStore();
        await GetAuthQrCode_WithValidToken_ReturnsPngWithNoStore();
    }

    private async Task StartAsync_ListensOnProvidedAddressAndRandomPort()
    {
        await using var service = CreateService();

        var session = await service.StartAsync((_, _) =>
            Task.FromResult(LanCookieRelaySubmitResult.Succeeded("ok")));

        Assert.Equal(IPAddress.Loopback.ToString(), session.Host);
        Assert.True(session.Port > 0);
        Assert.StartsWith("http://127.0.0.1:", session.Url.ToString(), StringComparison.Ordinal);
        Assert.Contains("token=", session.Url.Query);
    }

    private async Task GetRoot_ReturnsMobileHtmlWithNoStore()
    {
        await using var service = CreateService();
        var session = await service.StartAsync((_, _) =>
            Task.FromResult(LanCookieRelaySubmitResult.Succeeded("ok")));
        using var client = new HttpClient();

        using var response = await client.GetAsync(session.Url);
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl?.NoStore);
        Assert.Contains("局域网快传", html);
        Assert.Contains("微信授权二维码", html);
        Assert.Contains("/auth-qrcode?token=' + encodeURIComponent(token)", html);
        Assert.Contains("navigator.clipboard.readText", html);
    }

    private async Task GetAuthQrCode_WithValidToken_ReturnsPngWithNoStore()
    {
        await using var service = CreateService();
        var session = await service.StartAsync((_, _) =>
            Task.FromResult(LanCookieRelaySubmitResult.Succeeded("ok")));
        using var client = new HttpClient();

        using var response = await client.GetAsync(BuildAuthQrCodeUri(session));
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
    public async Task AuthenticationAndMethodGuards_RejectEveryInvalidRequest()
    {
        await GetAuthQrCode_WithInvalidToken_IsRejected();
        await PostSubmit_WithInvalidToken_IsRejectedWithoutCallingHandler();
        await NonPostSubmit_IsRejectedWithoutCallingHandler();
    }

    private async Task GetAuthQrCode_WithInvalidToken_IsRejected()
    {
        await using var service = CreateService();
        var session = await service.StartAsync((_, _) =>
            Task.FromResult(LanCookieRelaySubmitResult.Succeeded("ok")));
        using var client = new HttpClient();

        using var response = await client.GetAsync(BuildAuthQrCodeUri(session, "bad-token"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostSubmit_WithValidToken_ReturnsSuccessJsonAndStopsService()
    {
        await using var service = CreateService();
        var stopped = TrackStopped(service);
        string? submittedLink = null;
        var session = await service.StartAsync((link, _) =>
        {
            submittedLink = link;
            return Task.FromResult(LanCookieRelaySubmitResult.Succeeded("授权成功"));
        });
        using var client = new HttpClient();

        using var response = await client.PostAsync(
            BuildSubmitUri(session),
            JsonContent("""{"link":"https://example.com/?code=1234567890abcdef1234567890abcdef"}"""));
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("https://example.com/?code=1234567890abcdef1234567890abcdef", submittedLink);
        AssertJsonResult(json, success: true, "授权成功");
        Assert.Equal(LanCookieRelayStopReason.Submitted, await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task PostSubmit_WhenSubmittedConcurrently_ProcessesOnlyFirstRequest()
    {
        await using var service = CreateService();
        var stopped = TrackStopped(service);
        var handlerStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseHandler = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var handlerCalls = 0;
        var session = await service.StartAsync(async (_, _) =>
        {
            Interlocked.Increment(ref handlerCalls);
            handlerStarted.TrySetResult();
            await releaseHandler.Task;
            return LanCookieRelaySubmitResult.Succeeded("授权成功");
        });
        using var client = new HttpClient();

        var firstPost = client.PostAsync(
            BuildSubmitUri(session),
            JsonContent("""{"link":"https://example.com/?code=1234567890abcdef1234567890abcdef"}"""));
        await handlerStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        using var secondResponse = await client.PostAsync(
            BuildSubmitUri(session),
            JsonContent("""{"link":"https://example.com/?code=fedcba0987654321fedcba0987654321"}"""));
        releaseHandler.SetResult();
        using var firstResponse = await firstPost;

        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        Assert.Equal(1, handlerCalls);
        Assert.Equal(LanCookieRelayStopReason.Submitted, await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task PostSubmit_WhenHandlerFails_ReturnsFailureJsonAndKeepsServiceRunning()
    {
        await using var service = CreateService();
        var stopped = TrackStopped(service);
        var handlerCalls = 0;
        var session = await service.StartAsync((_, _) =>
        {
            handlerCalls++;
            return Task.FromResult(handlerCalls == 1
                ? LanCookieRelaySubmitResult.Failed("链接无效")
                : LanCookieRelaySubmitResult.Succeeded("授权成功"));
        });
        using var client = new HttpClient();

        using var response = await client.PostAsync(
            BuildSubmitUri(session),
            JsonContent("""{"link":"bad"}"""));
        var json = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        AssertJsonResult(json, success: false, "链接无效");
        Assert.False(stopped.Task.IsCompleted);

        using var retryResponse = await client.PostAsync(
            BuildSubmitUri(session),
            JsonContent("""{"link":"https://example.com/?code=1234567890abcdef1234567890abcdef"}"""));
        var retryJson = await retryResponse.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        AssertJsonResult(retryJson, success: true, "授权成功");
        Assert.Equal(2, handlerCalls);
        Assert.Equal(LanCookieRelayStopReason.Submitted, await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    private async Task PostSubmit_WithInvalidToken_IsRejectedWithoutCallingHandler()
    {
        await using var service = CreateService();
        var handlerCalls = 0;
        var session = await service.StartAsync((_, _) =>
        {
            handlerCalls++;
            return Task.FromResult(LanCookieRelaySubmitResult.Succeeded("ok"));
        });
        using var client = new HttpClient();

        using var response = await client.PostAsync(
            BuildSubmitUri(session, "bad-token"),
            JsonContent("""{"link":"https://example.com"}"""));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(0, handlerCalls);
    }

    private async Task NonPostSubmit_IsRejectedWithoutCallingHandler()
    {
        await using var service = CreateService();
        var handlerCalls = 0;
        var session = await service.StartAsync((_, _) =>
        {
            handlerCalls++;
            return Task.FromResult(LanCookieRelaySubmitResult.Succeeded("ok"));
        });
        using var client = new HttpClient();

        using var response = await client.GetAsync(BuildSubmitUri(session));

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, handlerCalls);
    }

    [Fact]
    public async Task PostSubmit_WithOversizedBody_IsRejectedWithoutCallingHandlerAndKeepsServiceRunning()
    {
        await using var service = CreateService();
        var stopped = TrackStopped(service);
        var handlerCalls = 0;
        var session = await service.StartAsync((_, _) =>
        {
            handlerCalls++;
            return Task.FromResult(LanCookieRelaySubmitResult.Succeeded("ok"));
        });
        using var client = new HttpClient();
        var oversized = new string('x', 9 * 1024);

        HttpResponseMessage? rejectionResponse = null;
        var rejectionException = await Record.ExceptionAsync(async () =>
        {
            rejectionResponse = await client.PostAsync(
                BuildSubmitUri(session),
                new StringContent(oversized, Encoding.UTF8, "text/plain"));
        });

        if (rejectionException is null)
        {
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, rejectionResponse!.StatusCode);
        }
        else
        {
            Assert.IsType<HttpRequestException>(rejectionException);
        }

        rejectionResponse?.Dispose();
        Assert.Equal(0, handlerCalls);

        using var retryResponse = await client.PostAsync(
            BuildSubmitUri(session),
            JsonContent("""{"link":"https://example.com/?code=1234567890abcdef1234567890abcdef"}"""));

        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.Equal(1, handlerCalls);
        Assert.Equal(LanCookieRelayStopReason.Submitted, await stopped.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void SelectPrimaryLanAddress_PrefersPhysicalLanAddressOverVirtualAdapter()
    {
        var selected = LanAddressProvider.SelectPrimaryLanAddress(
        [
            new LanAddressCandidate(
                IPAddress.Parse("172.20.0.10"),
                NetworkInterfaceType.Ethernet,
                HasGateway: true,
                IsLikelyVirtual: true,
                Speed: 10_000_000_000),
            new LanAddressCandidate(
                IPAddress.Parse("192.168.31.189"),
                NetworkInterfaceType.Wireless80211,
                HasGateway: true,
                IsLikelyVirtual: false,
                Speed: 866_000_000)
        ]);

        Assert.Equal(IPAddress.Parse("192.168.31.189"), selected);
    }

    [Fact]
    public void SelectPrimaryLanAddress_UsesVirtualAdapterAsFallback()
    {
        var selected = LanAddressProvider.SelectPrimaryLanAddress(
        [
            new LanAddressCandidate(
                IPAddress.Parse("172.20.0.10"),
                NetworkInterfaceType.Ethernet,
                HasGateway: true,
                IsLikelyVirtual: true,
                Speed: 10_000_000_000)
        ]);

        Assert.Equal(IPAddress.Parse("172.20.0.10"), selected);
    }

    [Fact]
    public async Task CloudflareMode_UsesPublicEndpointAndSupportsLiveSwitching()
    {
        await StartAsync_InCloudflareMode_ReturnsPublicUrlAndKeepsLanUrl();
        await ActiveSession_LiveModeSwitchChangesOnlyPublishedEndpoint();
    }

    private async Task StartAsync_InCloudflareMode_ReturnsPublicUrlAndKeepsLanUrl()
    {
        var exposureManager = new FakeNetworkExposureManager();
        exposureManager.Initialize(MobileControlNetworkMode.CloudflareTunnel, CloudflareTunnelProxyMode.Auto, string.Empty);
        await using var service = CreateService(exposureManager);

        var session = await service.StartAsync((_, _) =>
            Task.FromResult(LanCookieRelaySubmitResult.Succeeded("ok")));

        Assert.Equal(MobileControlNetworkMode.CloudflareTunnel, session.EffectiveMode);
        Assert.Equal("https", session.Url.Scheme);
        Assert.Equal("unit-test.trycloudflare.com", session.Url.Host);
        Assert.Equal("http", session.LanUrl.Scheme);
        Assert.Equal(session.LanUrl.Query, session.Url.Query);
    }

    private async Task ActiveSession_LiveModeSwitchChangesOnlyPublishedEndpoint()
    {
        var exposureManager = new FakeNetworkExposureManager();
        await using var service = CreateService(exposureManager);
        var original = await service.StartAsync((_, _) =>
            Task.FromResult(LanCookieRelaySubmitResult.Succeeded("ok")));
        LanCookieRelaySession? changed = null;
        service.EndpointChanged += (_, e) => changed = e.Session;

        await exposureManager.SetModeAsync(MobileControlNetworkMode.CloudflareTunnel);

        var updated = Assert.IsType<LanCookieRelaySession>(changed);
        Assert.Equal(original.SessionId, updated.SessionId);
        Assert.Equal(original.Port, updated.Port);
        Assert.Equal(original.LanUrl, updated.LanUrl);
        Assert.Equal("https", updated.Url.Scheme);
    }

    private static LanCookieRelayService CreateService(INetworkExposureManager? exposureManager = null)
    {
        return new LanCookieRelayService(
            new FixedLanAddressProvider(IPAddress.Loopback),
            exposureManager ?? new FakeNetworkExposureManager(),
            NullLogger<LanCookieRelayService>.Instance);
    }

    private static Uri BuildSubmitUri(LanCookieRelaySession session, string? token = null)
    {
        var builder = new UriBuilder(new Uri(session.Url, "/submit"))
        {
            Query = token is null ? session.Url.Query.TrimStart('?') : $"token={Uri.EscapeDataString(token)}"
        };
        return builder.Uri;
    }

    private static Uri BuildAuthQrCodeUri(LanCookieRelaySession session, string? token = null)
    {
        var builder = new UriBuilder(new Uri(session.Url, "/auth-qrcode"))
        {
            Query = token is null ? session.Url.Query.TrimStart('?') : $"token={Uri.EscapeDataString(token)}"
        };
        return builder.Uri;
    }

    private static StringContent JsonContent(string json)
    {
        return new StringContent(json, Encoding.UTF8, "application/json");
    }

    private static TaskCompletionSource<LanCookieRelayStopReason> TrackStopped(ILanCookieRelayService service)
    {
        var stopped = new TaskCompletionSource<LanCookieRelayStopReason>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        service.Stopped += (_, args) => stopped.TrySetResult(args.Reason);
        return stopped;
    }

    private static void AssertJsonResult(string json, bool success, string message)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(success, document.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(message, document.RootElement.GetProperty("message").GetString());
    }

    private sealed class FixedLanAddressProvider(IPAddress address) : ILanAddressProvider
    {
        public IPAddress? GetPrimaryLanAddress()
        {
            return address;
        }
    }
}
