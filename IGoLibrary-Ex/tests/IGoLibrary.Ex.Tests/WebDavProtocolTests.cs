using System.Net;
using System.Net.Security;
using IGoLibrary.Ex.Application.Backup;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Infrastructure.DataTransfer;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class WebDavProtocolTests
{
    [Fact]
    public void CreateHandler_OnlySkipsCertificateValidationWhenExplicitlyConfigured()
    {
        using var verifyHandler = WebDavClient.CreateHandler(
            string.Empty,
            null,
            WebDavTlsVerifyMode.Verify);
        using var skipHandler = WebDavClient.CreateHandler(
            string.Empty,
            null,
            WebDavTlsVerifyMode.Skip);

        Assert.Null(verifyHandler.SslOptions.RemoteCertificateValidationCallback);
        Assert.True(skipHandler.SslOptions.RemoteCertificateValidationCallback!(
            null!,
            null!,
            null!,
            SslPolicyErrors.RemoteCertificateChainErrors));
    }

    [Fact]
    public void BuildFileUri_EncodesEveryRemotePathSegment()
    {
        var uri = WebDavClient.BuildFileUri(
            new Uri("https://dav.example.com/root/"),
            "备份 目录/data+#.igobackup");

        Assert.Equal(
            "https://dav.example.com/root/%E5%A4%87%E4%BB%BD%20%E7%9B%AE%E5%BD%95/data%2B%23.igobackup",
            uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://dav.example.com/a", "https://dav.example.com/b", true)]
    [InlineData("https://dav.example.com/a", "http://dav.example.com/b", false)]
    [InlineData("https://dav.example.com/a", "https://other.example.com/b", false)]
    [InlineData("https://dav.example.com:8443/a", "https://dav.example.com/b", false)]
    public void RedirectOriginValidation_BlocksCrossOriginAndTlsDowngrade(
        string source,
        string target,
        bool expected)
    {
        Assert.Equal(
            expected,
            WebDavClient.HasSameOrigin(new Uri(source), new Uri(target)));
    }

    [Fact]
    public void PropFindParser_ReadsMetadataWithoutResolvingExternalEntities()
    {
        var requestedUri = new Uri("https://dav.example.com/root/backup.igobackup");
        const string xml =
            "<?xml version=\"1.0\"?><d:multistatus xmlns:d=\"DAV:\"><d:response>" +
            "<d:href>/root/backup.igobackup</d:href><d:propstat><d:prop>" +
            "<d:getcontentlength>2048</d:getcontentlength><d:getetag>\"abc\"</d:getetag>" +
            "<d:getlastmodified>Sat, 18 Jul 2026 08:00:00 GMT</d:getlastmodified>" +
            "</d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response></d:multistatus>";

        var result = WebDavClient.ParsePropFindMetadata(xml, requestedUri);

        Assert.True(result.Exists);
        Assert.Equal(2048, result.ContentLength);
        Assert.Equal("\"abc\"", result.ETag);
        Assert.Equal(new DateTimeOffset(2026, 7, 18, 8, 0, 0, TimeSpan.Zero), result.LastModified);
    }

    [Fact]
    public void PropFindParser_RejectsDtd()
    {
        const string xml =
            "<!DOCTYPE d:multistatus [<!ENTITY xxe SYSTEM \"file:///secret\">]>" +
            "<d:multistatus xmlns:d=\"DAV:\"><d:getetag>&xxe;</d:getetag></d:multistatus>";

        Assert.ThrowsAny<Exception>(() => WebDavClient.ParsePropFindMetadata(
            xml,
            new Uri("https://dav.example.com/backup.igobackup")));
    }

    [Fact]
    public void PropFindParser_SelectsRequestedHrefAndSuccessfulPropStat()
    {
        var requestedUri = new Uri("https://dav.example.com/root/backup.igobackup");
        const string xml =
            "<?xml version=\"1.0\"?><d:multistatus xmlns:d=\"DAV:\">" +
            "<d:response><d:href>/root/other.igobackup</d:href><d:propstat><d:prop>" +
            "<d:getcontentlength>999</d:getcontentlength><d:getetag>\"other\"</d:getetag>" +
            "</d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>" +
            "<d:response><d:href>https://dav.example.com/root/backup.igobackup</d:href>" +
            "<d:propstat><d:prop><d:getcontentlength>1</d:getcontentlength>" +
            "</d:prop><d:status>HTTP/1.1 404 Not Found</d:status></d:propstat>" +
            "<d:propstat><d:prop><d:getcontentlength>2048</d:getcontentlength>" +
            "<d:getetag>W/\"target\"</d:getetag>" +
            "<d:getlastmodified>Sat, 18 Jul 2026 08:00:00 GMT</d:getlastmodified>" +
            "</d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>" +
            "</d:multistatus>";

        var result = WebDavClient.ParsePropFindMetadata(xml, requestedUri);

        Assert.True(result.Exists);
        Assert.Equal(2048, result.ContentLength);
        Assert.Equal("W/\"target\"", result.ETag);
        Assert.Equal(new DateTimeOffset(2026, 7, 18, 8, 0, 0, TimeSpan.Zero), result.LastModified);
    }

    [Fact]
    public void PropFindParser_RejectsMultiStatusWithoutRequestedHref()
    {
        const string xml =
            "<?xml version=\"1.0\"?><d:multistatus xmlns:d=\"DAV:\"><d:response>" +
            "<d:href>/root/other.igobackup</d:href><d:propstat><d:prop/>" +
            "<d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response></d:multistatus>";

        Assert.Throws<InvalidDataException>(() => WebDavClient.ParsePropFindMetadata(
            xml,
            new Uri("https://dav.example.com/root/backup.igobackup")));
    }

    [Fact]
    public async Task ConnectionProbe_WritesReadsVerifiesAndDeletesProbeFile()
    {
        var handler = new ProbeHandler("IGoLibrary-Ex");
        using var client = new HttpClient(handler);
        var webDav = new WebDavClient(
            TimeProvider.System,
            NullLogger<WebDavClient>.Instance);

        await webDav.ProbeWriteAsync(
            client,
            new Uri("https://dav.example.com/root/backup.igobackup"),
            CancellationToken.None);

        Assert.Equal("IGoLibrary-Ex", handler.UploadedContent);
        Assert.Equal([HttpMethod.Put, HttpMethod.Get, HttpMethod.Delete], handler.Methods);
        Assert.Single(handler.RequestUris.Distinct());
        Assert.Contains("/.igolibrary-ex-probe-", handler.RequestUris[0].AbsolutePath);
    }

    [Fact]
    public async Task ConnectionProbe_RejectsDifferentDownloadedContentAndStillDeletesProbeFile()
    {
        var handler = new ProbeHandler("IGoLibrary-No");
        using var client = new HttpClient(handler);
        var webDav = new WebDavClient(
            TimeProvider.System,
            NullLogger<WebDavClient>.Instance);

        await Assert.ThrowsAsync<InvalidDataException>(() => webDav.ProbeWriteAsync(
            client,
            new Uri("https://dav.example.com/root/backup.igobackup"),
            CancellationToken.None));

        Assert.Equal([HttpMethod.Put, HttpMethod.Get, HttpMethod.Delete], handler.Methods);
    }

    [Fact]
    public async Task MetadataReader_StopsAtTheConfiguredResponseLimit()
    {
        var stream = new CountingReadStream(2 * 1024 * 1024);
        using var client = new HttpClient(new SingleResponseHandler(() => new HttpResponseMessage(
            (HttpStatusCode)207)
        {
            Content = new StreamContent(stream)
        }));
        var webDav = new WebDavClient(
            TimeProvider.System,
            NullLogger<WebDavClient>.Instance);

        await Assert.ThrowsAsync<InvalidDataException>(() => webDav.GetMetadataAsync(
            client,
            new Uri("https://dav.example.com/root/backup.igobackup"),
            CancellationToken.None));

        Assert.InRange(stream.BytesRead, 1024 * 1024 + 1, 1024 * 1024 + 16 * 1024);
    }

    [Fact]
    public void CreatedHttpClient_HasABoundedRequestTimeout()
    {
        var webDav = new WebDavClient(
            TimeProvider.System,
            NullLogger<WebDavClient>.Instance);

        using var client = webDav.CreateHttpClient(
            string.Empty,
            null,
            WebDavTlsVerifyMode.Verify);

        Assert.Equal(WebDavClient.RequestTimeout, client.Timeout);
        Assert.NotEqual(Timeout.InfiniteTimeSpan, client.Timeout);
    }

    [Theory]
    [InlineData("\"strong\"", true)]
    [InlineData("W/\"weak\"", false)]
    [InlineData("not-an-etag", false)]
    [InlineData(null, false)]
    public void StrongEtagDetection_NeverTreatsWeakOrMalformedValidatorsAsStrong(
        string? value,
        bool expected)
    {
        Assert.Equal(expected, WebDavClient.TryGetStrongETag(value, out _));
    }

    [Fact]
    public void BaselineComparison_WhenServerAddsAnEtag_RequiresContentHash()
    {
        var timestamp = new DateTimeOffset(2026, 7, 18, 8, 0, 0, TimeSpan.Zero);
        var remote = new WebDavRemoteMetadata(
            true,
            2048,
            "\"new-validator\"",
            timestamp);
        var state = new WebDavSyncState(
            "endpoint",
            null,
            timestamp,
            2048,
            new string('A', 64),
            "local",
            timestamp);

        Assert.Equal(
            WebDavBaselineComparison.RequiresContentHash,
            WebDavSyncService.CompareRemoteBaseline(remote, state));
    }

    [Theory]
    [InlineData("\"same\"", "\"same\"", (int)WebDavBaselineComparison.StrongMatch)]
    [InlineData("\"old\"", "\"new\"", (int)WebDavBaselineComparison.Mismatch)]
    [InlineData("W/\"same\"", "W/\"same\"", (int)WebDavBaselineComparison.RequiresContentHash)]
    public void BaselineComparison_OnlyTrustsMatchingStrongEtags(
        string current,
        string baseline,
        int expectedValue)
    {
        var remote = new WebDavRemoteMetadata(true, 10, current, null);
        var state = new WebDavSyncState(
            "endpoint",
            baseline,
            null,
            10,
            new string('B', 64),
            "local",
            null);

        Assert.Equal(
            (WebDavBaselineComparison)expectedValue,
            WebDavSyncService.CompareRemoteBaseline(remote, state));
    }

    private sealed class SingleResponseHandler(Func<HttpResponseMessage> responseFactory)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = responseFactory();
            response.RequestMessage = request;
            return Task.FromResult(response);
        }
    }

    private sealed class ProbeHandler(string downloadedContent) : HttpMessageHandler
    {
        public List<HttpMethod> Methods { get; } = [];

        public List<Uri> RequestUris { get; } = [];

        public string? UploadedContent { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Methods.Add(request.Method);
            RequestUris.Add(request.RequestUri!);
            if (request.Method == HttpMethod.Put)
            {
                UploadedContent = await request.Content!.ReadAsStringAsync(cancellationToken);
                return new HttpResponseMessage(HttpStatusCode.Created);
            }

            if (request.Method == HttpMethod.Get)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(downloadedContent)
                };
            }

            if (request.Method == HttpMethod.Delete)
            {
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }

            throw new InvalidOperationException($"Unexpected HTTP method: {request.Method}");
        }
    }

    private sealed class CountingReadStream(long length) : Stream
    {
        private long _position;

        public long BytesRead => _position;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = (int)Math.Min(count, length - _position);
            if (read <= 0)
            {
                return 0;
            }

            Array.Fill(buffer, (byte)'x', offset, read);
            _position += read;
            return read;
        }

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var read = (int)Math.Min(buffer.Length, length - _position);
            if (read <= 0)
            {
                return ValueTask.FromResult(0);
            }

            buffer.Span[..read].Fill((byte)'x');
            _position += read;
            return ValueTask.FromResult(read);
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
