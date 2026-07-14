using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Infrastructure.Updates;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class GitHubReleaseAssetDownloaderTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-downloader-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task DownloadAsync_StreamsReportsAndAtomicallyCompletes()
    {
        var bytes = "portable-update"u8.ToArray();
        var handler = new SequenceHttpMessageHandler((request, _) =>
        {
            Assert.Contains(request.Headers.AcceptEncoding, value => value.Value == "identity");
            return Task.FromResult(Response(bytes));
        });
        var downloader = new GitHubReleaseAssetDownloader(new HttpClient(handler));
        var progress = new List<ReleaseAssetDownloadProgress>();
        var destination = Path.Combine(_root, "package.zip");

        await downloader.DownloadAsync(
            Asset(bytes),
            destination,
            new InlineProgress<ReleaseAssetDownloadProgress>(progress.Add));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        Assert.False(File.Exists(destination + ".partial"));
        Assert.Contains(progress, value =>
            value.State == ReleaseAssetDownloadState.Downloading &&
            value.DownloadedBytes == bytes.Length);
        Assert.Equal(ReleaseAssetDownloadState.Verifying, progress[^1].State);
    }

    [Fact]
    public async Task DownloadAsync_ResumesExistingPartialWithValidatedRange()
    {
        var bytes = "portable-update"u8.ToArray();
        var prefixLength = 5;
        var destination = Path.Combine(_root, "package.zip");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(destination + ".partial", bytes[..prefixLength]);
        var handler = new SequenceHttpMessageHandler((request, _) =>
        {
            AssertRange(request, prefixLength);
            return Task.FromResult(PartialResponse(
                bytes[prefixLength..],
                prefixLength,
                bytes.Length - 1,
                bytes.Length));
        });

        await new GitHubReleaseAssetDownloader(new HttpClient(handler))
            .DownloadAsync(Asset(bytes), destination);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        Assert.Equal(1, handler.CallCount);
        Assert.False(File.Exists(destination + ".partial"));
    }

    [Fact]
    public async Task DownloadAsync_AcceptsMultipleValidatedPartialSegments()
    {
        var bytes = "0123456789"u8.ToArray();
        var destination = Path.Combine(_root, "package.zip");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(destination + ".partial", bytes[..2]);
        var handler = new SequenceHttpMessageHandler(
            (request, _) =>
            {
                AssertRange(request, 2);
                return Task.FromResult(PartialResponse(bytes[2..6], 2, 5, bytes.Length));
            },
            (request, _) =>
            {
                AssertRange(request, 6);
                return Task.FromResult(PartialResponse(bytes[6..], 6, 9, bytes.Length));
            });

        await new GitHubReleaseAssetDownloader(new HttpClient(handler))
            .DownloadAsync(Asset(bytes), destination);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task DownloadAsync_WhenRangeIsIgnored_DiscardsOldBytesAndRestarts()
    {
        var bytes = "fresh-package"u8.ToArray();
        var destination = Path.Combine(_root, "package.zip");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(destination + ".partial", "stale"u8.ToArray());
        var handler = new SequenceHttpMessageHandler((request, _) =>
        {
            AssertRange(request, 5);
            return Task.FromResult(Response(bytes));
        });

        await new GitHubReleaseAssetDownloader(new HttpClient(handler))
            .DownloadAsync(Asset(bytes), destination);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task DownloadAsync_Invalid416_CleansPartialAndStartsFresh()
    {
        var bytes = "fresh-package"u8.ToArray();
        var destination = Path.Combine(_root, "package.zip");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(destination + ".partial", bytes[..3]);
        var handler = new SequenceHttpMessageHandler(
            (request, _) =>
            {
                AssertRange(request, 3);
                return Task.FromResult(StatusResponse(HttpStatusCode.RequestedRangeNotSatisfiable));
            },
            (request, _) =>
            {
                Assert.Null(request.Headers.Range);
                return Task.FromResult(Response(bytes));
            });

        await new GitHubReleaseAssetDownloader(new HttpClient(handler))
            .DownloadAsync(Asset(bytes), destination);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task DownloadAsync_416AfterPartialBecameComplete_VerifiesWithoutRestart()
    {
        var bytes = "complete-during-request"u8.ToArray();
        var destination = Path.Combine(_root, "package.zip");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(destination + ".partial", bytes[..3]);
        var handler = new SequenceHttpMessageHandler((request, _) =>
        {
            AssertRange(request, 3);
            File.WriteAllBytes(destination + ".partial", bytes);
            return Task.FromResult(StatusResponse(HttpStatusCode.RequestedRangeNotSatisfiable));
        });

        await new GitHubReleaseAssetDownloader(new HttpClient(handler))
            .DownloadAsync(Asset(bytes), destination);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task DownloadAsync_CompletePartial_IsVerifiedWithoutNetworkRequest()
    {
        var bytes = "complete-package"u8.ToArray();
        var destination = Path.Combine(_root, "package.zip");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(destination + ".partial", bytes);
        var handler = new SequenceHttpMessageHandler();

        await new GitHubReleaseAssetDownloader(new HttpClient(handler))
            .DownloadAsync(Asset(bytes), destination);

        Assert.Equal(0, handler.CallCount);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
    }

    [Fact]
    public async Task DownloadAsync_ReadFailure_RetriesFromPreservedOffset()
    {
        var bytes = "resumable-package"u8.ToArray();
        var prefixLength = 6;
        var first = StreamResponse(
            new PrefixThenThrowStream(bytes[..prefixLength]),
            bytes.Length);
        var handler = new SequenceHttpMessageHandler(
            (_, _) => Task.FromResult(first),
            (request, _) =>
            {
                AssertRange(request, prefixLength);
                return Task.FromResult(PartialResponse(
                    bytes[prefixLength..],
                    prefixLength,
                    bytes.Length - 1,
                    bytes.Length));
            });
        var destination = Path.Combine(_root, "package.zip");

        await CreateFastRetryDownloader(handler).DownloadAsync(Asset(bytes), destination);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task DownloadAsync_PauseCancelsRead_PreservesAndResumesFromOffset()
    {
        var bytes = "pause-resume-package"u8.ToArray();
        var prefixLength = 7;
        var handler = new SequenceHttpMessageHandler(
            (_, _) => Task.FromResult(StreamResponse(
                new PrefixThenWaitStream(bytes[..prefixLength]),
                bytes.Length)),
            (request, _) =>
            {
                AssertRange(request, prefixLength);
                return Task.FromResult(PartialResponse(
                    bytes[prefixLength..],
                    prefixLength,
                    bytes.Length - 1,
                    bytes.Length));
            });
        var destination = Path.Combine(_root, "package.zip");
        var downloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var paused = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var controller = new ReleaseAssetDownloadPauseController();
        var progress = new InlineProgress<ReleaseAssetDownloadProgress>(value =>
        {
            if (value.State == ReleaseAssetDownloadState.Downloading &&
                value.DownloadedBytes == prefixLength)
            {
                downloaded.TrySetResult();
            }

            if (value.State == ReleaseAssetDownloadState.Paused)
            {
                paused.TrySetResult();
            }
        });

        var task = CreateFastRetryDownloader(handler).DownloadAsync(
            Asset(bytes),
            destination,
            progress,
            pauseSource: controller);
        await downloaded.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(controller.TryPause());
        await paused.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(prefixLength, new FileInfo(destination + ".partial").Length);
        Assert.True(controller.TryResume());
        await task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task DownloadAsync_RetriesThreeTimesThenReturnsRecoverableInterruption()
    {
        var handler = new SequenceHttpMessageHandler(
            (_, _) => Task.FromResult(NeverProgressResponse()),
            (_, _) => Task.FromResult(NeverProgressResponse()),
            (_, _) => Task.FromResult(NeverProgressResponse()),
            (_, _) => Task.FromResult(NeverProgressResponse()));
        var downloader = new GitHubReleaseAssetDownloader(
            new HttpClient(handler),
            NullLogger<GitHubReleaseAssetDownloader>.Instance,
            TimeProvider.System,
            TimeSpan.FromMilliseconds(20),
            [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]);
        var destination = Path.Combine(_root, "package.zip");

        var exception = await Assert.ThrowsAsync<ReleaseAssetDownloadInterruptedException>(() =>
            downloader.DownloadAsync(
                new ReleaseAssetInfo(
                    "IGoLibrary-Ex-v1.0.1-windows-x64.zip",
                    new Uri("https://github.com/EJianZQ/IGoLibrary/releases/download/v1.0.1/file.zip"),
                    1,
                    "sha256:" + new string('0', 64),
                    "application/zip"),
                destination));

        Assert.Equal(4, handler.CallCount);
        Assert.Equal(0, exception.PreservedBytes);
        Assert.False(exception.CanResume);
        Assert.True(File.Exists(destination + ".partial"));
    }

    [Theory]
    [InlineData(408)]
    [InlineData(425)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task DownloadAsync_TransientHttpStatusIsRetried(int statusCode)
    {
        var bytes = "retry-status"u8.ToArray();
        var handler = new SequenceHttpMessageHandler(
            (_, _) => Task.FromResult(StatusResponse((HttpStatusCode)statusCode)),
            (_, _) => Task.FromResult(Response(bytes)));

        await CreateFastRetryDownloader(handler).DownloadAsync(
            Asset(bytes),
            Path.Combine(_root, "package.zip"));

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task DownloadAsync_RetryAfterOverridesDefaultBackoff()
    {
        var bytes = "retry-after"u8.ToArray();
        var retryResponse = StatusResponse(HttpStatusCode.ServiceUnavailable);
        retryResponse.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
        var handler = new SequenceHttpMessageHandler(
            (_, _) => Task.FromResult(retryResponse),
            (_, _) => Task.FromResult(Response(bytes)));
        var downloader = new GitHubReleaseAssetDownloader(
            new HttpClient(handler),
            NullLogger<GitHubReleaseAssetDownloader>.Instance,
            TimeProvider.System,
            TimeSpan.FromSeconds(1),
            [TimeSpan.FromSeconds(5), TimeSpan.Zero, TimeSpan.Zero]);

        await downloader.DownloadAsync(Asset(bytes), Path.Combine(_root, "package.zip"))
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task DownloadAsync_EarlyEndOfStreamResumesFromPreservedOffset()
    {
        var bytes = "early-eof-package"u8.ToArray();
        var prefixLength = 4;
        var handler = new SequenceHttpMessageHandler(
            (_, _) => Task.FromResult(StreamResponse(
                new MemoryStream(bytes[..prefixLength]),
                bytes.Length)),
            (request, _) =>
            {
                AssertRange(request, prefixLength);
                return Task.FromResult(PartialResponse(
                    bytes[prefixLength..],
                    prefixLength,
                    bytes.Length - 1,
                    bytes.Length));
            });
        var destination = Path.Combine(_root, "package.zip");

        await CreateFastRetryDownloader(handler).DownloadAsync(Asset(bytes), destination);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task DownloadAsync_NewBytesResetConsecutiveFailureBudget()
    {
        var bytes = "reset-budget"u8.ToArray();
        var prefixLength = 3;
        var handler = new SequenceHttpMessageHandler(
            (_, _) => Task.FromResult(NeverProgressResponse(bytes.Length)),
            (_, _) => Task.FromResult(StreamResponse(
                new PrefixThenThrowStream(bytes[..prefixLength]),
                bytes.Length)),
            (request, _) =>
            {
                AssertRange(request, prefixLength);
                return Task.FromResult(PartialStreamResponse(
                    new NeverProgressStream(),
                    prefixLength,
                    bytes.Length - 1,
                    bytes.Length));
            },
            (request, _) =>
            {
                AssertRange(request, prefixLength);
                return Task.FromResult(PartialStreamResponse(
                    new NeverProgressStream(),
                    prefixLength,
                    bytes.Length - 1,
                    bytes.Length));
            },
            (request, _) =>
            {
                AssertRange(request, prefixLength);
                return Task.FromResult(PartialResponse(
                    bytes[prefixLength..],
                    prefixLength,
                    bytes.Length - 1,
                    bytes.Length));
            });
        var downloader = new GitHubReleaseAssetDownloader(
            new HttpClient(handler),
            NullLogger<GitHubReleaseAssetDownloader>.Instance,
            TimeProvider.System,
            TimeSpan.FromMilliseconds(20),
            [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]);
        var destination = Path.Combine(_root, "package.zip");

        await downloader.DownloadAsync(Asset(bytes), destination);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        Assert.Equal(5, handler.CallCount);
    }

    [Fact]
    public async Task DownloadAsync_UserCancellationDeletesPartialFile()
    {
        var requestStarted = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new SequenceHttpMessageHandler((_, _) =>
        {
            requestStarted.TrySetResult();
            return Task.FromResult(NeverProgressResponse());
        });
        var destination = Path.Combine(_root, "package.zip");
        using var cancellation = new CancellationTokenSource();
        var task = CreateFastRetryDownloader(handler).DownloadAsync(
            new ReleaseAssetInfo(
                "IGoLibrary-Ex-v1.0.1-windows-x64.zip",
                new Uri("https://github.com/EJianZQ/IGoLibrary/releases/download/v1.0.1/file.zip"),
                1,
                "sha256:" + new string('0', 64),
                "application/zip"),
            destination,
            cancellationToken: cancellation.Token);
        await requestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(File.Exists(destination + ".partial"));
    }

    [Fact]
    public async Task DownloadAsync_NonHttpsFinalAddressDeletesPartialFile()
    {
        var bytes = "insecure-redirect"u8.ToArray();
        var response = Response(bytes);
        response.RequestMessage = new HttpRequestMessage(
            HttpMethod.Get,
            "http://objects.githubusercontent.com/release-assets/file.zip");
        var destination = Path.Combine(_root, "package.zip");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new GitHubReleaseAssetDownloader(new HttpClient(
                    new SequenceHttpMessageHandler((_, _) => Task.FromResult(response))))
                .DownloadAsync(Asset(bytes), destination));

        Assert.False(File.Exists(destination + ".partial"));
    }

    [Fact]
    public async Task DownloadAsync_RejectsResponseLengthMismatch()
    {
        var bytes = "short"u8.ToArray();
        var response = Response(bytes);
        response.Content.Headers.ContentLength = bytes.Length + 1;
        var downloader = new GitHubReleaseAssetDownloader(new HttpClient(
            new SequenceHttpMessageHandler((_, _) => Task.FromResult(response))));
        var destination = Path.Combine(_root, "package.zip");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadAsync(Asset(bytes), destination));

        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".partial"));
    }

    [Fact]
    public async Task DownloadAsync_RejectsMissingResponseLength()
    {
        var bytes = "chunked"u8.ToArray();
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new UnknownLengthContent(bytes),
            RequestMessage = FinalRequest()
        };
        var downloader = new GitHubReleaseAssetDownloader(new HttpClient(
            new SequenceHttpMessageHandler((_, _) => Task.FromResult(response))));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadAsync(Asset(bytes), Path.Combine(_root, "package.zip")));

        Assert.Contains("Content-Length", exception.Message);
    }

    [Fact]
    public async Task DownloadAsync_RejectsDigestMismatchAndDeletesPartialFile()
    {
        var bytes = "wrong digest"u8.ToArray();
        var asset = Asset(bytes) with
        {
            Digest = "sha256:" + new string('0', 64)
        };
        var downloader = new GitHubReleaseAssetDownloader(new HttpClient(
            new SequenceHttpMessageHandler((_, _) => Task.FromResult(Response(bytes)))));
        var destination = Path.Combine(_root, "package.zip");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadAsync(asset, destination));

        Assert.False(File.Exists(destination));
        Assert.False(File.Exists(destination + ".partial"));
    }

    [Fact]
    public async Task DownloadAsync_RejectsMalformedRangeAndDeletesPartial()
    {
        var bytes = "range-package"u8.ToArray();
        var destination = Path.Combine(_root, "package.zip");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(destination + ".partial", bytes[..3]);
        var response = PartialResponse(bytes[3..], 2, bytes.Length - 1, bytes.Length);
        var downloader = new GitHubReleaseAssetDownloader(new HttpClient(
            new SequenceHttpMessageHandler((_, _) => Task.FromResult(response))));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadAsync(Asset(bytes), destination));

        Assert.False(File.Exists(destination + ".partial"));
    }

    [Fact]
    public async Task DownloadAsync_NonRetryableHttpFailureDeletesPartial()
    {
        var bytes = "not-found-package"u8.ToArray();
        var destination = Path.Combine(_root, "package.zip");
        Directory.CreateDirectory(_root);
        await File.WriteAllBytesAsync(destination + ".partial", bytes[..4]);
        var downloader = new GitHubReleaseAssetDownloader(new HttpClient(
            new SequenceHttpMessageHandler((_, _) =>
                Task.FromResult(StatusResponse(HttpStatusCode.NotFound)))));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            downloader.DownloadAsync(Asset(bytes), destination));

        Assert.False(File.Exists(destination + ".partial"));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
        }
    }

    private static GitHubReleaseAssetDownloader CreateFastRetryDownloader(
        SequenceHttpMessageHandler handler)
    {
        return new GitHubReleaseAssetDownloader(
            new HttpClient(handler),
            NullLogger<GitHubReleaseAssetDownloader>.Instance,
            TimeProvider.System,
            TimeSpan.FromSeconds(1),
            [TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero]);
    }

    private static HttpResponseMessage Response(byte[] bytes)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
            RequestMessage = FinalRequest()
        };
    }

    private static HttpResponseMessage PartialResponse(
        byte[] bytes,
        long from,
        long to,
        long total)
    {
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(bytes),
            RequestMessage = FinalRequest()
        };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, total);
        return response;
    }

    private static HttpResponseMessage PartialStreamResponse(
        Stream stream,
        long from,
        long to,
        long total)
    {
        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new StreamContent(stream),
            RequestMessage = FinalRequest()
        };
        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, total);
        response.Content.Headers.ContentLength = to - from + 1;
        return response;
    }

    private static HttpResponseMessage StreamResponse(Stream stream, long contentLength)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(stream),
            RequestMessage = FinalRequest()
        };
        response.Content.Headers.ContentLength = contentLength;
        return response;
    }

    private static HttpResponseMessage NeverProgressResponse()
    {
        return StreamResponse(new NeverProgressStream(), 1);
    }

    private static HttpResponseMessage NeverProgressResponse(long contentLength)
    {
        return StreamResponse(new NeverProgressStream(), contentLength);
    }

    private static HttpResponseMessage StatusResponse(HttpStatusCode statusCode)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent([]),
            RequestMessage = FinalRequest()
        };
    }

    private static HttpRequestMessage FinalRequest()
    {
        return new HttpRequestMessage(
            HttpMethod.Get,
            "https://objects.githubusercontent.com/release-assets/file.zip");
    }

    private static ReleaseAssetInfo Asset(byte[] bytes)
    {
        return new ReleaseAssetInfo(
            "IGoLibrary-Ex-v1.0.1-windows-x64.zip",
            new Uri("https://github.com/EJianZQ/IGoLibrary/releases/download/v1.0.1/file.zip"),
            bytes.Length,
            "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)),
            "application/zip");
    }

    private static void AssertRange(HttpRequestMessage request, long expectedFrom)
    {
        Assert.NotNull(request.Headers.Range);
        var range = Assert.Single(request.Headers.Range!.Ranges);
        Assert.Equal(expectedFrom, range.From);
        Assert.Null(range.To);
        Assert.Contains(request.Headers.AcceptEncoding, value => value.Value == "identity");
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private abstract class TestReadStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class NeverProgressStream : TestReadStream
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class PrefixThenThrowStream(byte[] prefix) : TestReadStream
    {
        private bool _prefixReturned;

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!_prefixReturned)
            {
                _prefixReturned = true;
                prefix.CopyTo(buffer);
                return ValueTask.FromResult(prefix.Length);
            }

            return ValueTask.FromException<int>(new IOException("simulated disconnect"));
        }
    }

    private sealed class PrefixThenWaitStream(byte[] prefix) : TestReadStream
    {
        private bool _prefixReturned;

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!_prefixReturned)
            {
                _prefixReturned = true;
                prefix.CopyTo(buffer);
                return prefix.Length;
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            return stream.WriteAsync(bytes.AsMemory()).AsTask();
        }

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
