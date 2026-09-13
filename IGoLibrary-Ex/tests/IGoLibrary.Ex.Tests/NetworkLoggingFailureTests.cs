using System.Net;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Logging;
using IGoLibrary.Ex.Infrastructure.Logging;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Tests;

public sealed class NetworkLoggingFailureTests
{
    [Fact]
    public void CaptureFaultsAreIsolatedAndThrottledUsingInjectedTime()
    {
        var writer = new NetworkLogTestWriter();
        var time = new FakeTimeProvider();
        var reporter = new NetworkLogFaultReporter(writer, time);
        reporter.Report(new IOException("private-secret"));
        reporter.Report(new IOException("private-secret"));
        Assert.Single(writer.Entries);
        time.Advance(TimeSpan.FromMinutes(1));
        reporter.Report(new IOException("private-secret"));
        Assert.Equal(2, writer.Entries.Count);
        Assert.Contains("本次合并数量=1", writer.Text);
        Assert.DoesNotContain("private-secret", writer.Text);
    }
    [Fact]
    public async Task ThrowingLogWriterCannotChangeSuccessfulHttpOperation()
    {
        var state = new NetworkLogState();
        state.Apply(true);
        var handler = new NetworkLoggingHandler(new NetworkTrafficLogger(state, new ThrowingWriter()))
        {
            InnerHandler = new NetworkTestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("response") }))
        };
        using var client = new HttpClient(handler);
        Assert.Equal("response", await client.GetStringAsync("https://a.test"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailingResponseStreamPreservesExceptionAndRecordsOneTerminal(bool synchronous)
    {
        var logs = new NetworkLogTestContext();
        var expected = new IOException("private-secret");
        using var content = new StreamContent(new FailingStream(expected));
        content.Headers.ContentType = new("text/plain");
        using var client = logs.Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content }));
        using var response = await client.GetAsync("https://a.test", HttpCompletionOption.ResponseHeadersRead);
        using var stream = await response.Content.ReadAsStreamAsync();
        if (synchronous) Assert.Same(expected, Assert.Throws<IOException>(() => stream.Read(new byte[1])));
        else Assert.Same(expected, await Assert.ThrowsAsync<IOException>(async () => { _ = await stream.ReadAsync(new byte[1]); }));
        response.Dispose();
        Assert.Single(logs.Writer.Entries, e => e.Message.Contains("响应结束"));
        Assert.Contains(logs.Writer.Entries, e => e.Level == LogLevel.Warning && e.Message.Contains("读取失败"));
        Assert.DoesNotContain("private-secret", logs.Writer.Text);
    }

    [Fact]
    public void BinaryAndOversizedCapturesRetainNoPayloadBuffer()
    {
        var binary = new NetworkBodyCapture(() => true, () => false);
        binary.Append(new byte[65536]);
        Assert.Equal(0, binary.BufferedBytes);
        var large = new NetworkBodyCapture(() => true);
        large.Append(new byte[65536]);
        Assert.Equal(65536, large.BufferedBytes);
        large.Append([1]);
        Assert.Equal(0, large.BufferedBytes);
        large.Append([2]);
        Assert.Equal(0, large.BufferedBytes);
    }

    [Fact]
    public void DisabledGenerationClearsCapturedBytesAndNeverResumes()
    {
        var logs = new NetworkLogTestContext();
        var session = logs.Logger.Start("test", "out", "HTTP", "https://a.test")!;
        var body = session.CreateBody();
        body.Append("private-secret"u8);
        logs.State.Apply(false);
        logs.State.Apply(true);
        body.Append("new-data"u8);
        Assert.Equal(0, body.BufferedBytes);
        body.Complete();
        var text = body.Render("text/plain");
        Assert.Contains("设置关闭", text);
        Assert.DoesNotContain("private-secret", text);
    }

    [Fact]
    public async Task ConcurrentRequestsHaveDistinctStableCorrelationIds()
    {
        var logs = new NetworkLogTestContext();
        using var client = logs.Client((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        { Content = new StringContent("response") }));
        await Task.WhenAll(Enumerable.Range(0, 25).Select(async i =>
        {
            using var response = await client.GetAsync($"https://a.test/{i}");
        }));
        var groups = logs.Writer.Entries.GroupBy(e => e.Message.Split('；')[0]).ToArray();
        Assert.Equal(25, groups.Length);
        foreach (var group in groups)
        {
            Assert.Single(group, e => e.Message.Contains("开始；"));
            Assert.Single(group, e => e.Message.Contains("响应结束；"));
        }
    }

    private sealed class ThrowingWriter : IAppLogWriter
    {
        public void Write(LogLevel level, string category, string message, Exception? exception = null, EventId eventId = default, DateTimeOffset? timestamp = null) => throw new IOException("disk");
        public void Flush() { }
    }

    private sealed class FailingStream(Exception exception) : Stream
    {
        public override bool CanRead => true;
        public override bool CanWrite => false;
        public override bool CanSeek => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw exception;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => throw exception;
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
