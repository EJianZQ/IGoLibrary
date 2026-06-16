using System.Net;
using IGoLibrary.Ex.Infrastructure.Updates;

namespace IGoLibrary.Ex.Tests;

public sealed class GitHubReleaseClientTests
{
    [Fact]
    public async Task GetReleasesAsync_MapsGitHubReleaseJson()
    {
        var handler = new SequenceHttpMessageHandler(
            (_, _) => SequenceHttpMessageHandler.JsonResponseAsync(
                """
                [
                  {
                    "tag_name": "v1.0.0",
                    "name": "IGoLibrary-Ex v1.0.0",
                    "body": "更新内容",
                    "html_url": "https://github.com/EJianZQ/IGoLibrary/releases/tag/v1.0.0",
                    "published_at": "2026-06-09T08:00:00Z",
                    "draft": false,
                    "prerelease": false,
                    "assets": [
                      { "name": "IGoLibrary-Ex-v1.0.0-windows-x64.zip" }
                    ]
                  }
                ]
                """));
        var client = new GitHubReleaseClient(new HttpClient(handler));

        var result = await client.GetReleasesAsync(null, TimeSpan.FromSeconds(3));

        var release = Assert.Single(result.Releases);
        Assert.False(result.NotModified);
        Assert.Equal("v1.0.0", release.TagName);
        Assert.Equal("IGoLibrary-Ex v1.0.0", release.Name);
        Assert.Equal("更新内容", release.Body);
        Assert.Equal(new DateTimeOffset(2026, 6, 9, 8, 0, 0, TimeSpan.Zero), release.PublishedAt);
        Assert.Contains("IGoLibrary-Ex-v1.0.0-windows-x64.zip", release.AssetNames);
    }

    [Fact]
    public async Task GetReleasesAsync_SendsIfNoneMatch_AndHandlesNotModified()
    {
        var handler = new SequenceHttpMessageHandler((request, _) =>
        {
            Assert.True(request.Headers.IfNoneMatch.Any());
            Assert.Equal("\"cached\"", request.Headers.IfNoneMatch.First().ToString());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotModified)
            {
                Headers =
                {
                    ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"cached\"")
                }
            });
        });
        var client = new GitHubReleaseClient(new HttpClient(handler));

        var result = await client.GetReleasesAsync("\"cached\"", TimeSpan.FromSeconds(3));

        Assert.True(result.NotModified);
        Assert.Empty(result.Releases);
        Assert.Equal("\"cached\"", result.ETag);
    }

    [Fact]
    public async Task GetReleasesAsync_ThrowsTimeoutException_WhenRequestExceedsTimeout()
    {
        var handler = new SequenceHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new GitHubReleaseClient(new HttpClient(handler));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            client.GetReleasesAsync(null, TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public async Task GetReleasesAsync_ThrowsTimeoutException_WhenResponseBodyExceedsTimeout()
    {
        var handler = new SequenceHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new BlockingReadStream())
        }));
        var client = new GitHubReleaseClient(new HttpClient(handler));

        await Assert.ThrowsAsync<TimeoutException>(() =>
            client.GetReleasesAsync(null, TimeSpan.FromMilliseconds(20)));
    }

    [Fact]
    public async Task GetReleasesAsync_PreservesOperationCanceledException_WhenCallerCancels()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        var handler = new SequenceHttpMessageHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        var client = new GitHubReleaseClient(new HttpClient(handler));

        cancellationTokenSource.CancelAfter(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetReleasesAsync(null, TimeSpan.FromSeconds(5), cancellationTokenSource.Token));
    }

    private sealed class BlockingReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override async Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return 0;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
