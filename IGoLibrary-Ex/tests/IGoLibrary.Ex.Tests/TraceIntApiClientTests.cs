using System.Net;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Api;

namespace IGoLibrary.Ex.Tests;

public sealed class TraceIntApiClientTests
{
    [Fact]
    public async Task GetLibrariesAsync_RetriesTransientHttpFailures_UsingSavedRetryCount()
    {
        var handler = new SequenceHttpMessageHandler(
            (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("retry-1", null, HttpStatusCode.ServiceUnavailable)),
            (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("retry-2", null, HttpStatusCode.BadGateway)),
            (_, _) => SequenceHttpMessageHandler.JsonResponseAsync("""
                {"data":{"userAuth":{"reserve":{"libs":[{"lib_id":1,"lib_name":"自科阅览区一","lib_floor":"3","is_open":true}]}}}}
                """));

        var client = new TraceIntApiClient(
            new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            },
            new FakeProtocolTemplateStore(new ProtocolTemplateSet(
                "https://example.com/ReplaceMeByCode",
                "{\"query\":\"libraries\"}",
                "{\"query\":\"layout\"}",
                "{\"query\":\"rule\"}",
                "{\"query\":\"reservation\"}",
                "{\"query\":\"reserve\"}",
                "{\"query\":\"cancel\"}")),
            new FakeSettingsService(AppSettings.Default with { ApiTimeoutSeconds = 1, RetryCount = 2 }));

        var libraries = await client.GetLibrariesAsync("Authorization=a; SERVERID=b");

        Assert.Single(libraries);
        Assert.Equal(3, handler.CallCount);
        Assert.Equal("自科阅览区一", libraries[0].Name);
    }

    [Fact]
    public async Task GetLibrariesAsync_RetriesTimedOutRequest_UsingSavedTimeoutSetting()
    {
        var handler = new SequenceHttpMessageHandler(
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                return await SequenceHttpMessageHandler.JsonResponseAsync("{}");
            },
            (_, _) => SequenceHttpMessageHandler.JsonResponseAsync("""
                {"data":{"userAuth":{"reserve":{"libs":[{"lib_id":2,"lib_name":"社科阅览区","lib_floor":"5","is_open":true}]}}}}
                """));

        var client = new TraceIntApiClient(
            new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            },
            new FakeProtocolTemplateStore(new ProtocolTemplateSet(
                "https://example.com/ReplaceMeByCode",
                "{\"query\":\"libraries\"}",
                "{\"query\":\"layout\"}",
                "{\"query\":\"rule\"}",
                "{\"query\":\"reservation\"}",
                "{\"query\":\"reserve\"}",
                "{\"query\":\"cancel\"}")),
            new FakeSettingsService(AppSettings.Default with { ApiTimeoutSeconds = 1, RetryCount = 1 }));

        var libraries = await client.GetLibrariesAsync("Authorization=a; SERVERID=b");

        Assert.Single(libraries);
        Assert.Equal(2, handler.CallCount);
        Assert.Equal(2, libraries[0].LibraryId);
    }

    [Fact]
    public async Task GetLibraryRuleAsync_ParsesRulePayload()
    {
        var handler = new SequenceHttpMessageHandler(
            (_, _) => SequenceHttpMessageHandler.JsonResponseAsync("""
                {
                  "data": {
                    "userAuth": {
                      "reserve": {
                        "libRule": {
                          "advance_booking": "1小时",
                          "lib_seat_ttl": "30",
                          "lib_hold_ttl": "30",
                          "lib_renew_time": "0",
                          "hold_reason": "{\"1\":{\"reason\":\"暂离保留\",\"time\":1800}}",
                          "close_start_date": null,
                          "close_end_date": null,
                          "open_time": 1774740600,
                          "open_time_str": "7:30",
                          "close_time": 1774792800,
                          "close_time_str": "22:00",
                          "lib_validate_time": -1
                        }
                      }
                    }
                  }
                }
                """));

        var client = new TraceIntApiClient(
            new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            },
            new FakeProtocolTemplateStore(new ProtocolTemplateSet(
                "https://example.com/ReplaceMeByCode",
                "{\"query\":\"libraries\"}",
                "{\"query\":\"layout\"}",
                "{\"query\":\"rule\"}",
                "{\"query\":\"reservation\"}",
                "{\"query\":\"reserve\"}",
                "{\"query\":\"cancel\"}")),
            new FakeSettingsService(AppSettings.Default));

        var rule = await client.GetLibraryRuleAsync("Authorization=a; SERVERID=b", 117580);

        Assert.Equal(117580, rule.LibraryId);
        Assert.Equal("1小时", rule.AdvanceBooking);
        Assert.Equal("30", rule.SeatTtlMinutes);
        Assert.Equal("30", rule.HoldTtlMinutes);
        Assert.Equal("0", rule.RenewTimeMinutes);
        Assert.Equal("7:30", rule.OpenTimeText);
        Assert.Equal("22:00", rule.CloseTimeText);
        Assert.Equal(-1, rule.ValidateTime);
    }

    [Fact]
    public async Task GetLibrariesAsync_ThrowsStructuredTraceIntApiException_ForExpiredCookieResponse()
    {
        var handler = new SequenceHttpMessageHandler(
            (_, _) => SequenceHttpMessageHandler.JsonResponseAsync("""
                {
                  "errors": [
                    {
                      "msg": "access denied!",
                      "code": 40001
                    }
                  ],
                  "data": {
                    "userAuth": null
                  }
                }
                """));

        var client = new TraceIntApiClient(
            new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            },
            new FakeProtocolTemplateStore(new ProtocolTemplateSet(
                "https://example.com/ReplaceMeByCode",
                "{\"query\":\"libraries\"}",
                "{\"query\":\"layout\"}",
                "{\"query\":\"rule\"}",
                "{\"query\":\"reservation\"}",
                "{\"query\":\"reserve\"}",
                "{\"query\":\"cancel\"}")),
            new FakeSettingsService(AppSettings.Default));

        var exception = await Assert.ThrowsAsync<TraceIntApiException>(() => client.GetLibrariesAsync("Authorization=a; SERVERID=b"));

        Assert.Equal(40001, exception.ErrorCode);
        Assert.Equal("access denied!", exception.RemoteMessage);
        Assert.True(exception.IsAuthorizationDenied);
    }

    [Fact]
    public void BuildCookieHeaderFromResponseCookies_MatchesWinformOrdering()
    {
        var cookies = TraceIntApiClient.BuildCookieHeaderFromResponseCookies(
        [
            "SERVERID=b9fc7bd86d2eed91b23d7347e0ee995e|1775746288|1775746288",
            "Authorization=eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9"
        ]);

        Assert.Equal(
            "Authorization=eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9; SERVERID=b9fc7bd86d2eed91b23d7347e0ee995e|1775746288|1775746288",
            cookies);
    }

    [Fact]
    public void BuildCookieHeaderFromResponseCookies_Throws_WhenCookieCollectionIsNull()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => TraceIntApiClient.BuildCookieHeaderFromResponseCookies(null));

        Assert.Equal("响应报文返回的Cookie为空", exception.Message);
    }

    [Fact]
    public void BuildCookieHeaderFromResponseCookies_Throws_WhenCookieCollectionHasFewerThanTwoItems()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => TraceIntApiClient.BuildCookieHeaderFromResponseCookies(
        [
            "Authorization=eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9"
        ]));

        Assert.Equal("Cookie不包含关键身份信息，可能是code过期，重新填写含code的链接", exception.Message);
    }
}
