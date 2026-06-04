using System.Net;
using IGoLibrary.Ex.Application.Exceptions;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Api;
using RestSharp;

namespace IGoLibrary.Ex.Tests;

public sealed class TraceIntApiClientTests
{
    [Fact]
    public async Task GetLibrariesAsync_RetriesTransientHttpFailures_UsingSavedMaxRetries()
    {
        var handler = new SequenceHttpMessageHandler(
            (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("retry-1", null, HttpStatusCode.ServiceUnavailable)),
            (_, _) => Task.FromException<HttpResponseMessage>(new HttpRequestException("retry-2", null, HttpStatusCode.BadGateway)),
            (_, _) => SequenceHttpMessageHandler.JsonResponseAsync("""
                {"data":{"userAuth":{"reserve":{"libs":[{"lib_id":1,"lib_name":"自科阅览区一","lib_floor":"3","is_open":true}]}}}}
                """));

        var client = CreateClient(handler, AppSettings.Default with { Network = new NetworkRequestSettings(1, 2) });

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

        var client = CreateClient(handler, AppSettings.Default with { Network = new NetworkRequestSettings(1, 1) });

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

        var client = CreateClient(handler);

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
    public async Task GetLibraryLayoutAsync_KeepsTypeOneSeatsWithNonNumericNames_AndSkipsLayoutObjects()
    {
        var handler = new SequenceHttpMessageHandler(
            (_, _) => SequenceHttpMessageHandler.JsonResponseAsync("""
                {
                  "data": {
                    "userAuth": {
                      "reserve": {
                        "libs": [
                          {
                            "lib_id": 117580,
                            "lib_name": "自科阅览区一",
                            "lib_floor": "3",
                            "is_open": true,
                            "lib_layout": {
                              "seats_total": 4,
                              "seats_booking": 0,
                              "seats_used": 1,
                              "seats": [
                                { "x": 0, "y": 0, "key": "seat-12", "type": 1, "name": "12", "seat_status": 1, "status": false },
                                { "x": 1, "y": 0, "key": "seat-a12", "type": 1, "name": "A12", "seat_status": 1, "status": false },
                                { "x": 2, "y": 0, "key": "seat-room-1", "type": 1, "name": "研修间1", "seat_status": 1, "status": true },
                                { "x": 3, "y": 0, "key": "seat-fallback", "type": 1, "name": "", "seat_status": 1, "status": false },
                                { "x": 4, "y": 0, "key": "", "type": 1, "name": "无效座位", "seat_status": 1, "status": false },
                                { "x": 5, "y": 0, "key": "pillar", "type": 8, "name": "柱", "seat_status": 0, "status": false },
                                { "x": 6, "y": 0, "key": "west-label", "type": 8, "name": "西", "seat_status": 0, "status": false },
                                { "x": 7, "y": 0, "key": "desk", "type": 6, "name": null, "seat_status": 0, "status": false },
                                { "key": "layout-label", "name": "区域标签" }
                              ]
                            }
                          }
                        ]
                      }
                    }
                  }
                }
                """));

        var client = CreateClient(handler);

        var layout = await client.GetLibraryLayoutAsync("Authorization=a; SERVERID=b", 117580);

        Assert.Equal(4, layout.Seats.Count);
        Assert.Contains(layout.Seats, seat => seat.SeatKey == "seat-12" && seat.SeatName == "12");
        Assert.Contains(layout.Seats, seat => seat.SeatKey == "seat-a12" && seat.SeatName == "A12");
        Assert.Contains(layout.Seats, seat => seat.SeatKey == "seat-room-1" && seat.SeatName == "研修间1" && seat.IsOccupied);
        Assert.Contains(layout.Seats, seat => seat.SeatKey == "seat-fallback" && seat.SeatName == "seat-fallback");
        Assert.DoesNotContain(layout.Seats, seat => seat.SeatName == "无效座位");
        Assert.DoesNotContain(layout.Seats, seat => seat.SeatName == "柱");
        Assert.DoesNotContain(layout.Seats, seat => seat.SeatName == "西");
        Assert.DoesNotContain(layout.Seats, seat => seat.SeatKey == "desk");
        Assert.DoesNotContain(layout.Seats, seat => seat.SeatKey == "layout-label");
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

        var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<TraceIntApiException>(() => client.GetLibrariesAsync("Authorization=a; SERVERID=b"));

        Assert.Equal(40001, exception.ErrorCode);
        Assert.Equal("access denied!", exception.RemoteMessage);
        Assert.True(exception.IsAuthorizationDenied);
    }

    [Fact]
    public async Task GetCookieFromCodeAsync_RetriesTransientHttpFailure_UsingSavedMaxRetries()
    {
        var cookieHttpClient = new FakeTraceIntCookieHttpClient(
            (_, _) => Task.FromResult(CookieResponse(HttpStatusCode.ServiceUnavailable)),
            (_, _) => Task.FromResult(CookieResponse(
                HttpStatusCode.OK,
                "SERVERID=b",
                "Authorization=a")));

        var client = CreateClient(
            new SequenceHttpMessageHandler(),
            AppSettings.Default with { Network = new NetworkRequestSettings(1, 1) },
            cookieHttpClient);

        var cookie = await client.GetCookieFromCodeAsync("code-1");

        Assert.Equal("Authorization=a; SERVERID=b", cookie);
        Assert.Equal(2, cookieHttpClient.CallCount);
    }

    [Fact]
    public async Task GetCookieFromCodeAsync_DoesNotRetryForbiddenResponse()
    {
        var cookieHttpClient = new FakeTraceIntCookieHttpClient(
            (_, _) => Task.FromResult(CookieResponse(HttpStatusCode.Forbidden)));

        var client = CreateClient(
            new SequenceHttpMessageHandler(),
            AppSettings.Default with { Network = new NetworkRequestSettings(1, 2) },
            cookieHttpClient);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetCookieFromCodeAsync("code-1"));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Equal(1, cookieHttpClient.CallCount);
    }

    [Fact]
    public async Task GetCookieFromCodeAsync_KeepsResponseCookieOrdering()
    {
        var cookieHttpClient = new FakeTraceIntCookieHttpClient(
            (_, _) => Task.FromResult(CookieResponse(
                HttpStatusCode.Found,
                "SERVERID=b9fc7bd86d2eed91b23d7347e0ee995e|1775746288|1775746288",
                "Authorization=eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9")));

        var client = CreateClient(
            new SequenceHttpMessageHandler(),
            cookieHttpClient: cookieHttpClient);

        var cookie = await client.GetCookieFromCodeAsync("code-1");

        Assert.Equal(
            "Authorization=eyJ0eXAiOiJKV1QiLCJhbGciOiJSUzI1NiJ9; SERVERID=b9fc7bd86d2eed91b23d7347e0ee995e|1775746288|1775746288",
            cookie);
        Assert.Equal(1, cookieHttpClient.CallCount);
    }

    [Fact]
    public async Task GetCookieFromCodeAsync_RetriesTimedOutRequest_UsingSavedTimeoutSetting()
    {
        var cookieHttpClient = new FakeTraceIntCookieHttpClient(
            async (_, cancellationToken) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                return CookieResponse(HttpStatusCode.OK);
            },
            (_, _) => Task.FromResult(CookieResponse(
                HttpStatusCode.OK,
                "SERVERID=b",
                "Authorization=a")));

        var client = CreateClient(
            new SequenceHttpMessageHandler(),
            AppSettings.Default with { Network = new NetworkRequestSettings(1, 1) },
            cookieHttpClient);

        var cookie = await client.GetCookieFromCodeAsync("code-1");

        Assert.Equal("Authorization=a; SERVERID=b", cookie);
        Assert.Equal(2, cookieHttpClient.CallCount);
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

    [Fact]
    public void ThrowIfCookieResponseFailed_ThrowsHttpRequestException_WhenStatusFailedWithoutCookies()
    {
        var response = new RestResponse
        {
            StatusCode = HttpStatusCode.Forbidden,
            StatusDescription = "Forbidden",
            ResponseStatus = ResponseStatus.Completed
        };

        var exception = Assert.Throws<HttpRequestException>(() => TraceIntApiClient.ThrowIfCookieResponseFailed(response, []));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Contains("HTTP 403 Forbidden", exception.Message);
    }

    [Fact]
    public void ThrowIfCookieResponseFailed_AllowsCookieExtraction_WhenFailedResponseAlreadyContainsCookies()
    {
        var response = new RestResponse
        {
            StatusCode = HttpStatusCode.Found,
            StatusDescription = "Found",
            ResponseStatus = ResponseStatus.Completed
        };

        TraceIntApiClient.ThrowIfCookieResponseFailed(response,
        [
            "SERVERID=b",
            "Authorization=a"
        ]);
    }

    private static TraceIntApiClient CreateClient(
        HttpMessageHandler handler,
        AppSettings? settings = null,
        ITraceIntCookieHttpClient? cookieHttpClient = null)
    {
        var httpClient = new HttpClient(handler)
        {
            Timeout = Timeout.InfiniteTimeSpan
        };
        var settingsService = new FakeSettingsService(settings ?? AppSettings.Default);
        var requestPolicy = new TraceIntRequestPolicy(settingsService);
        var graphQlTransport = new TraceIntGraphQlTransport(httpClient, requestPolicy);
        var protocolTemplateStore = new FakeProtocolTemplateStore(CreateTemplates());
        var cookieTransport = new TraceIntCookieTransport(
            protocolTemplateStore,
            requestPolicy,
            cookieHttpClient ?? new FakeTraceIntCookieHttpClient(
                (_, _) => Task.FromResult(CookieResponse(
                    HttpStatusCode.OK,
                    "SERVERID=b",
                    "Authorization=a"))));

        return new TraceIntApiClient(
            cookieTransport,
            protocolTemplateStore,
            graphQlTransport);
    }

    private static TraceIntGraphQlTemplates CreateTemplates()
    {
        return new TraceIntGraphQlTemplates(
            "https://example.com/ReplaceMeByCode",
            "{\"query\":\"libraries\"}",
            "{\"query\":\"layout\"}",
            "{\"query\":\"rule\"}",
            "{\"query\":\"reservation\"}",
            "{\"query\":\"reserve\"}",
            "{\"query\":\"cancel\"}");
    }

    private static TraceIntCookieHttpResponse CookieResponse(
        HttpStatusCode statusCode,
        params string[] cookies)
    {
        return new TraceIntCookieHttpResponse(
            new RestResponse
            {
                StatusCode = statusCode,
                StatusDescription = statusCode.ToString(),
                ResponseStatus = ResponseStatus.Completed
            },
            cookies);
    }

    private sealed class FakeTraceIntCookieHttpClient(
        params Func<string, CancellationToken, Task<TraceIntCookieHttpResponse>>[] steps) : ITraceIntCookieHttpClient
    {
        private readonly Queue<Func<string, CancellationToken, Task<TraceIntCookieHttpResponse>>> _steps = new(steps);

        public int CallCount { get; private set; }

        public Task<TraceIntCookieHttpResponse> ExecuteGetAsync(
            string requestUrl,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (_steps.Count == 0)
            {
                throw new InvalidOperationException("没有更多预设响应。");
            }

            return _steps.Dequeue().Invoke(requestUrl, cancellationToken);
        }
    }
}
