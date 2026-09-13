using System.Net;
using System.Text;
using IGoLibrary.Ex.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Tests;

public sealed class NetworkLoggingHttpTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(400)]
    [InlineData(500)]
    public async Task RecordsRequestAndResponseWithoutChangingBytes(int status)
    {
        var context = new NetworkLogTestContext();
        const string payload = "{\"password\":\"secret\",\"seat\":\"A001\"}";
        using var client = context.Client(async (request, ct) =>
        {
            Assert.Equal(payload, await request.Content!.ReadAsStringAsync(ct));
            return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(payload, Encoding.UTF8, "application/json") };
        });
        using var response = await client.PostAsync("https://a.test/path?code=secret", new StringContent(payload, Encoding.UTF8, "application/json"));
        Assert.Equal(payload, await response.Content.ReadAsStringAsync());
        response.Dispose();
        Assert.Contains("A001", context.Writer.Text);
        Assert.DoesNotContain("secret", context.Writer.Text);
        Assert.Single(context.Writer.Entries, e => e.Message.Contains("响应结束"));
        Assert.Contains($"状态码={status}", context.Writer.Text);
        if (status >= 400) Assert.Contains(context.Writer.Entries, e => e.Level == LogLevel.Warning);
    }

    [Fact]
    public async Task DisabledDoesNotWrapContentOrProduceLogs()
    {
        var context = new NetworkLogTestContext(false);
        using var original = new StringContent("body");
        using var client = context.Client((request, _) =>
        {
            Assert.Same(original, request.Content);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = original });
        });
        using var response = await client.PostAsync("https://a.test", original);
        Assert.Same(original, response.Content);
        Assert.Empty(context.Writer.Entries);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(6)]
    public async Task HeadersReadDoesNotPreconsumeStreamAndDisposeRecordsOnce(int readCount)
    {
        var context = new NetworkLogTestContext();
        using var stream = new CountingStream("abcdef"u8.ToArray());
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new("text/plain");
        using var client = context.Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var response = await client.GetAsync("https://a.test", HttpCompletionOption.ResponseHeadersRead);
        Assert.Equal(0, stream.ReadCount);
        var observed = await response.Content.ReadAsStreamAsync();
        Assert.Equal(0, stream.ReadCount);
        if (readCount > 0) await observed.ReadExactlyAsync(new byte[readCount]);
        Assert.Equal(readCount, stream.ReadCount);
        observed.Dispose();
        response.Dispose();
        Assert.Single(context.Writer.Entries, e => e.Message.Contains("响应结束"));
        Assert.Contains(readCount == 0 ? "未消费" : "仅部分消费", context.Writer.Text);
        Assert.DoesNotContain("abcdef", context.Writer.Text);
        Assert.Equal(readCount, stream.ReadCount);
    }

    [Fact]
    public async Task ReadStreamToEofPreservesNonSeekableContent()
    {
        var context = new NetworkLogTestContext();
        using var stream = new CountingStream(Encoding.UTF8.GetBytes("中文正文"));
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new("text/plain");
        using var client = context.Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var response = await client.GetAsync("https://a.test", HttpCompletionOption.ResponseHeadersRead);
        using var reader = new StreamReader(await response.Content.ReadAsStreamAsync());
        Assert.Equal("中文正文", await reader.ReadToEndAsync());
        Assert.Contains("中文正文", context.Writer.Text);
    }

    [Fact]
    public async Task DisablingAndReenablingDoesNotResurrectOldResponse()
    {
        var context = new NetworkLogTestContext();
        using var client = context.Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("old-response") }));
        using var response = await client.GetAsync("https://a.test", HttpCompletionOption.ResponseHeadersRead);
        context.State.Apply(false);
        context.State.Apply(true);
        Assert.Equal("old-response", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain("old-response", context.Writer.Text);
        using var next = await client.GetAsync("https://a.test");
        Assert.Contains("old-response", context.Writer.Text);
    }

    [Fact]
    public async Task FailureRethrowsOriginalAndDoesNotLogSecretExceptionMessage()
    {
        var context = new NetworkLogTestContext();
        var expected = new HttpRequestException("secret");
        using var client = context.Client((_, _) => throw expected);
        var actual = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetAsync("https://a.test"));
        Assert.Same(expected, actual);
        Assert.Contains("未收到响应", context.Writer.Text);
        Assert.DoesNotContain("secret", context.Writer.Text);
    }

    [Fact]
    public async Task CancellationIsPreserved()
    {
        var context = new NetworkLogTestContext();
        using var cts = new CancellationTokenSource();
        using var client = context.Client((_, ct) => { cts.Cancel(); ct.ThrowIfCancellationRequested(); throw new Exception(); });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://a.test", cts.Token));
        Assert.Contains(context.Writer.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("超时或取消"));
        Assert.DoesNotContain("用户取消", context.Writer.Text);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task DisposingContentReadStreamReleasesUnderlyingStream(bool synchronousReadStream, bool synchronousDispose)
    {
        var logs = new NetworkLogTestContext();
        var inner = new DisposeTrackingStream();
        using var client = logs.Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(inner) }));
        using var response = await client.GetAsync("https://a.test", HttpCompletionOption.ResponseHeadersRead);
        var stream = synchronousReadStream ? response.Content.ReadAsStream() : await response.Content.ReadAsStreamAsync();
        Assert.False(inner.Disposed);
        if (synchronousDispose) stream.Dispose(); else await stream.DisposeAsync();
        Assert.True(inner.Disposed);
        response.Dispose();
        Assert.Single(logs.Writer.Entries, e => e.Message.Contains("响应结束"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GetStreamAsyncKeepsItsOwnershipContract(bool enabled)
    {
        var logs = new NetworkLogTestContext(enabled);
        var inner = new DisposeTrackingStream();
        using var client = logs.Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(inner) }));
        var stream = await client.GetStreamAsync("https://a.test");
        await stream.DisposeAsync();
        Assert.True(inner.Disposed);
    }

    [Fact]
    public async Task SerializingContentLeavesBorrowedOutputStreamOpen()
    {
        var logs = new NetworkLogTestContext();
        using var client = logs.Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("payload") }));
        using var response = await client.GetAsync("https://a.test", HttpCompletionOption.ResponseHeadersRead);
        using var destination = new MemoryStream();
        await response.Content.CopyToAsync(destination);
        Assert.True(destination.CanWrite);
        Assert.Equal("payload", Encoding.UTF8.GetString(destination.ToArray()));
    }

    [Fact]
    public async Task HttpClientTimeoutIsNotReportedAsUserCancellation()
    {
        var logs = new NetworkLogTestContext();
        using var client = logs.Client(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        // HttpClient's own timeout has no injectable TimeProvider. Exercise its real linked token.
        client.Timeout = TimeSpan.FromMilliseconds(250);
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetAsync("https://a.test"));
        Assert.IsType<TimeoutException>(exception.InnerException);
        Assert.Contains(logs.Writer.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("超时或取消"));
        Assert.DoesNotContain("用户取消", logs.Writer.Text);
    }

    [Fact]
    public async Task RequestPolicyTimeoutIsNotReportedAsUserCancellation()
    {
        var logs = new NetworkLogTestContext();
        var time = new FakeTimeProvider();
        var settings = new FakeSettingsService(IGoLibrary.Ex.Application.Configuration.AppSettings.Default with
        {
            Network = new IGoLibrary.Ex.Application.Configuration.NetworkRequestSettings(1, 0)
        });
        var policy = new IGoLibrary.Ex.Infrastructure.Api.TraceIntRequestPolicy(settings, time);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var client = logs.Client(async (_, ct) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        client.Timeout = Timeout.InfiniteTimeSpan;
        var operation = policy.ExecuteOnceAsync(ct => client.GetStringAsync("https://a.test", ct), "测试请求", CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        time.Advance(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAsync<TimeoutException>(() => operation);
        Assert.Contains(logs.Writer.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("超时或取消"));
        Assert.DoesNotContain("用户取消", logs.Writer.Text);
    }

    private sealed class DisposeTrackingStream : MemoryStream
    {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes)
    {
        public int ReadCount { get; private set; }
        public override bool CanSeek => false;
        public override int Read(Span<byte> buffer) { var count = base.Read(buffer); ReadCount += count; return count; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => ValueTask.FromResult(Read(buffer.Span));
    }
}
