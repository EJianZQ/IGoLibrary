using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Api;

namespace IGoLibrary.Ex.Tests;

public sealed class RemoteCheckInProtocolTests
{
    private const string BeaconUuid = "E2C56DB5-DFFB-48D2-B060-D0F5A71096E0";

    [Fact]
    public async Task ApiClient_UsesConfiguredEndpointsCallbacksAndReferers()
    {
        var token = new string('a', 40);
        var handler = new SequenceHttpMessageHandler(
            (request, _) =>
            {
                Assert.Equal(
                    $"https://proxy.example.com/oauth/exchange?r=https%3A%2F%2Freturn.example.com%2Fdone%3Fx%3D1&code={new string('b', 32)}",
                    request.RequestUri!.AbsoluteUri);
                Assert.Equal("https://oauth-referer.example.com/start", request.Headers.Referrer!.AbsoluteUri);
                var response = new HttpResponseMessage(HttpStatusCode.SeeOther);
                response.Headers.TryAddWithoutValidation("Set-Cookie", $"wechatSESS_ID={token}; Path=/; HttpOnly");
                return Task.FromResult(response);
            },
            (request, _) =>
            {
                Assert.Equal("https://proxy.example.com/api/devices", request.RequestUri!.AbsoluteUri);
                Assert.Equal("https://mini.example.com/page-frame", request.Headers.Referrer!.AbsoluteUri);
                var json = JsonSerializer.Serialize(new
                {
                    code = 0,
                    msg = "",
                    data = new
                    {
                        user = new { user_nick = "N" },
                        devices = new[] { BeaconUuid }
                    }
                });
                return SequenceHttpMessageHandler.JsonResponseAsync(json);
            },
            (request, _) =>
            {
                Assert.Equal("https://proxy.example.com/api/time", request.RequestUri!.AbsoluteUri);
                Assert.Equal("https://mini.example.com/page-frame", request.Headers.Referrer!.AbsoluteUri);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("1782346868")
                });
            },
            (request, _) =>
            {
                Assert.Equal("https://proxy.example.com/api/sign", request.RequestUri!.AbsoluteUri);
                Assert.Equal("https://mini.example.com/page-frame", request.Headers.Referrer!.AbsoluteUri);
                return SequenceHttpMessageHandler.JsonResponseAsync("""{"code":0,"msg":"验证成功","data":{}}""");
            });
        var templates = CreateRemoteTemplates() with
        {
            RemoteCheckInAuthUrlTemplate = "https://proxy.example.com/oauth/exchange?r=ReplaceMeByReturnUrl&code=ReplaceMeByCode",
            RemoteCheckInAuthorizationReturnUrl = "https://return.example.com/done?x=1",
            RemoteCheckInAuthRefererUrl = "https://oauth-referer.example.com/start",
            RemoteCheckInDevicesEndpointUrl = "https://proxy.example.com/api/devices",
            RemoteCheckInTimeEndpointUrl = "https://proxy.example.com/api/time",
            RemoteCheckInSignEndpointUrl = "https://proxy.example.com/api/sign",
            RemoteCheckInApiRefererUrl = "https://mini.example.com/page-frame"
        };
        var client = CreateClient(handler, templates);

        await client.ExchangeOAuthCodeAsync(new string('b', 32));
        await client.GetDeviceInfoAsync(token);
        await client.GetServerTimeAsync();
        await client.SignAsync(token, CreateRequest());

        Assert.Equal(4, handler.CallCount);
    }

    [Fact]
    public void PayloadEncoder_MatchesDocumentedExamples()
    {
        var request = CreateRequest();

        Assert.Equal(
            "W1siRTJDNTZEQjUtREZGQi00OEQyLUIwNjAtRDBGNUE3MTA5NkUwIiwxMDAwMSwyMDAwMl1d",
            TraceIntRemoteCheckInPayloadEncoder.EncodeDevices(request));
        Assert.Equal(
            "WzM5LjkwODcyMiwxMTYuMzk3NDk5XQ==",
            TraceIntRemoteCheckInPayloadEncoder.EncodeLocation(request));
    }

    [Fact]
    public void EncryptTimestamp_UsesPkcs1AndUtf8()
    {
        using var rsa = RSA.Create(2048);
        var publicKey = rsa.ExportSubjectPublicKeyInfoPem();

        var encoded = TraceIntRemoteCheckInPayloadEncoder.EncryptTimestamp("1782346868", publicKey);
        var plaintext = rsa.Decrypt(Convert.FromBase64String(encoded), RSAEncryptionPadding.Pkcs1);

        Assert.Equal("1782346868", Encoding.UTF8.GetString(plaintext));
    }

    [Fact]
    public void BuiltInPublicKey_ProducesRsa2048Ciphertext()
    {
        var ciphertext = Convert.FromBase64String(
            TraceIntRemoteCheckInPayloadEncoder.EncryptTimestamp("1782346868"));

        Assert.Equal(256, ciphertext.Length);
    }

    [Fact]
    public async Task ExchangeOAuthCode_ExtractsNamedCookieWithoutDependingOnOrder()
    {
        var token = new string('a', 40);
        var handler = new SequenceHttpMessageHandler((request, _) =>
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Contains("wxApp/wechatAuth.html", request.RequestUri!.AbsoluteUri);
            Assert.Contains("miniProgram/wx3b9352e6b254ed2b", request.Headers.UserAgent.ToString());
            Assert.Equal("https://open.weixin.qq.com/", request.Headers.Referrer!.AbsoluteUri);
            var response = new HttpResponseMessage(HttpStatusCode.SeeOther);
            response.Headers.TryAddWithoutValidation("Set-Cookie", "SERVERID=test; Path=/");
            response.Headers.TryAddWithoutValidation("Set-Cookie", $"wechatSESS_ID={token}; Path=/; HttpOnly");
            return Task.FromResult(response);
        });
        var client = CreateClient(handler);

        var result = await client.ExchangeOAuthCodeAsync(new string('b', 32));

        Assert.Equal(token, result.Token);
        Assert.Null(result.ExpiresAt);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ExchangeOAuthCode_ExtractsQuoted48CharacterSessionCookie()
    {
        var token = new string('A', 48);
        var handler = new SequenceHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.SeeOther);
            response.Headers.TryAddWithoutValidation(
                "Set-Cookie",
                "SERVERID=test; Path=/");
            response.Headers.TryAddWithoutValidation(
                "Set-Cookie",
                $"\"wechatSESS_ID={token}; expires=Fri, 10-Jul-2026 02:33:04 GMT; path=/; domain=.traceint.com; httponly\"");
            return Task.FromResult(response);
        });
        var client = CreateClient(handler);

        var result = await client.ExchangeOAuthCodeAsync(new string('b', 32));

        Assert.Equal(token.ToLowerInvariant(), result.Token);
        Assert.Equal(48, result.Token.Length);
        Assert.Equal(
            new DateTimeOffset(2026, 7, 10, 2, 33, 4, TimeSpan.Zero),
            result.ExpiresAt);
    }

    [Fact]
    public async Task ExchangeOAuthCode_DoesNotRetryTransientFailure()
    {
        var handler = new SequenceHttpMessageHandler((_, _) =>
            throw new HttpRequestException("offline", null, HttpStatusCode.ServiceUnavailable));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.ExchangeOAuthCodeAsync(new string('b', 32)));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Devices_UsesFormTokenWithoutCookieHeaderAndRetriesReadFailure()
    {
        var handler = new SequenceHttpMessageHandler(
            (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)),
            async (request, cancellationToken) =>
            {
                Assert.Equal(HttpMethod.Post, request.Method);
                Assert.False(request.Headers.Contains("Cookie"));
                Assert.Equal(
                    "https://servicewechat.com/wx3b9352e6b254ed2b/25/page-frame.html",
                    request.Headers.Referrer!.AbsoluteUri);
                Assert.Equal($"t={new string('a', 40)}", await request.Content!.ReadAsStringAsync(cancellationToken));
                var json = JsonSerializer.Serialize(new
                {
                    code = 0,
                    msg = "",
                    data = new
                    {
                        user = new { user_nick = "N", user_sch = "S", user_student_no = "1", user_student_name = "U" },
                        devices = new[] { BeaconUuid.ToLowerInvariant(), BeaconUuid, "invalid" }
                    }
                });
                return await SequenceHttpMessageHandler.JsonResponseAsync(json);
            });
        var client = CreateClient(handler);

        var info = await client.GetDeviceInfoAsync(new string('a', 40));

        Assert.Equal([BeaconUuid], info.BeaconUuids);
        Assert.Equal(2, handler.CallCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Sign_ExplicitHttpFailureDoesNotRetryOrReportUnknownOutcome(HttpStatusCode statusCode)
    {
        var handler = new SequenceHttpMessageHandler(async (request, cancellationToken) =>
        {
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.False(request.Headers.Contains("Cookie"));
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            Assert.Contains("t=" + new string('a', 40), form);
            Assert.Contains("devices=", form);
            Assert.Contains("location=", form);
            Assert.Contains("pass=", form);
            return new HttpResponseMessage(statusCode);
        });
        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            client.SignAsync(new string('a', 40), CreateRequest()));
        Assert.Equal(statusCode, exception.StatusCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Sign_NetworkInterruptionReportsUnknownOutcomeWithoutRetry()
    {
        var handler = new SequenceHttpMessageHandler((_, _) =>
            throw new HttpRequestException("connection reset"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<RemoteCheckInOutcomeUnknownException>(() =>
            client.SignAsync(new string('a', 40), CreateRequest()));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task Sign_TimeoutReportsUnknownOutcomeWithoutRetry()
    {
        var handler = new SequenceHttpMessageHandler((_, _) =>
            throw new TimeoutException("timed out"));
        var client = CreateClient(handler);

        await Assert.ThrowsAsync<RemoteCheckInOutcomeUnknownException>(() =>
            client.SignAsync(new string('a', 40), CreateRequest()));

        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public void Mapper_NormalizesDevicesAndMapsSuccessfulSign()
    {
        var deviceJson = JsonSerializer.Serialize(new
        {
            code = 0,
            msg = "",
            data = new
            {
                user = new { user_nick = "Johnny", user_sch = "学校", user_student_no = "20240001", user_student_name = "李华" },
                devices = new[] { BeaconUuid.ToLowerInvariant() }
            }
        });
        var info = TraceIntRemoteCheckInResponseMapper.MapDeviceInfo(deviceJson);
        var result = TraceIntRemoteCheckInResponseMapper.MapSignResult(
            """{"code":0,"msg":"验证成功","data":{"status":2,"lib_id":101,"lib_name":"第三电子阅览室","lib_floor":"3楼","seat_key":"12,34","seat_name":"042","date":1782346716,"exp_date":1782348516}}""");

        Assert.Equal([BeaconUuid], info.BeaconUuids);
        Assert.Equal("李华", info.User.StudentName);
        Assert.Equal(101, result.LibraryId);
        Assert.Equal("042", result.SeatName);
        Assert.NotNull(result.SignedAt);
        Assert.NotNull(result.ExpirationTime);
    }

    [Theory]
    [InlineData("未登录")]
    [InlineData("会话已过期")]
    [InlineData("access denied")]
    public void Mapper_ClassifiesExplicitSessionFailures(string message)
    {
        var raw = JsonSerializer.Serialize(new { code = 1, msg = message, data = new { } });
        var ex = Assert.Throws<RemoteCheckInApiException>(() =>
            TraceIntRemoteCheckInResponseMapper.MapDeviceInfo(raw));

        Assert.True(ex.IsSessionInvalid);
    }

    [Fact]
    public void Mapper_RejectsInvalidServerTime()
    {
        Assert.Throws<RemoteCheckInApiException>(() =>
            TraceIntRemoteCheckInResponseMapper.MapServerTime("not-a-time"));
    }

    private static RemoteCheckInSignRequest CreateRequest()
        => new(BeaconUuid, 10001, 20002, 39.908722m, 116.397499m, "1782346868");

    private static TraceIntRemoteCheckInApiClient CreateClient(
        HttpMessageHandler handler,
        TraceIntProtocolTemplates? templates = null)
    {
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Network = new NetworkRequestSettings(5, 1)
        });
        var httpClient = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        return new TraceIntRemoteCheckInApiClient(
            new TraceIntRemoteCheckInTransport(httpClient, new TraceIntRequestPolicy(settingsService)),
            new FakeProtocolTemplateStore(templates ?? CreateRemoteTemplates()));
    }

    private static TraceIntProtocolTemplates CreateRemoteTemplates()
    {
        return TestProtocolTemplates.Create() with
        {
            RemoteCheckInAuthUrlTemplate = "https://wechat.v2.traceint.com/index.php/wxApp/wechatAuth.html?r=ReplaceMeByReturnUrl&code=ReplaceMeByCode&state=1",
            RemoteCheckInAuthorizationReturnUrl = "https://web.traceint.com/web/index.html",
            RemoteCheckInAuthRefererUrl = "https://open.weixin.qq.com/",
            RemoteCheckInDevicesEndpointUrl = "https://wechat.v2.traceint.com/index.php/wxApp/devices.html",
            RemoteCheckInTimeEndpointUrl = "https://wechat.v2.traceint.com/index.php/wxApp/getTime.html",
            RemoteCheckInSignEndpointUrl = "https://wechat.v2.traceint.com/index.php/wxApp/sign.html",
            RemoteCheckInApiRefererUrl = "https://servicewechat.com/wx3b9352e6b254ed2b/25/page-frame.html"
        };
    }
}
