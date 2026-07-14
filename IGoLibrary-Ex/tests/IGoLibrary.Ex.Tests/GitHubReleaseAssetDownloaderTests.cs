using System.Net;
using System.Security.Cryptography;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Infrastructure.Updates;

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
        var handler = new SequenceHttpMessageHandler((_, _) => Task.FromResult(Response(bytes)));
        var downloader = new GitHubReleaseAssetDownloader(new HttpClient(handler));
        var progress = new List<ReleaseAssetDownloadProgress>();
        var destination = Path.Combine(_root, "package.zip");

        await downloader.DownloadAsync(
            Asset(bytes),
            destination,
            new InlineProgress<ReleaseAssetDownloadProgress>(progress.Add));

        Assert.Equal(bytes, await File.ReadAllBytesAsync(destination));
        Assert.False(File.Exists(destination + ".partial"));
        Assert.Equal(bytes.Length, Assert.Single(progress).DownloadedBytes);
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
            RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                "https://objects.githubusercontent.com/release-assets/file.zip")
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
    public async Task DownloadAsync_TimesOutWhenStreamMakesNoProgress()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NeverProgressStream()),
            RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                "https://objects.githubusercontent.com/release-assets/file.zip")
        };
        response.Content.Headers.ContentLength = 1;
        var downloader = new GitHubReleaseAssetDownloader(
            new HttpClient(new SequenceHttpMessageHandler((_, _) => Task.FromResult(response))),
            TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAsync<TimeoutException>(() => downloader.DownloadAsync(
            new ReleaseAssetInfo(
                "IGoLibrary-Ex-v1.0.1-windows-x64.zip",
                new Uri("https://github.com/EJianZQ/IGoLibrary/releases/download/v1.0.1/file.zip"),
                1,
                "sha256:" + new string('0', 64),
                "application/zip"),
            Path.Combine(_root, "package.zip")));
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

    private static HttpResponseMessage Response(byte[] bytes)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes),
            RequestMessage = new HttpRequestMessage(
                HttpMethod.Get,
                "https://objects.githubusercontent.com/release-assets/file.zip")
        };
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

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class NeverProgressStream : Stream
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

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }

    private sealed class UnknownLengthContent(byte[] bytes) : HttpContent
    {
        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context)
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
