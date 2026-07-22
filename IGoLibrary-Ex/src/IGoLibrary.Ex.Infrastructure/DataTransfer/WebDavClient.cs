using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using IGoLibrary.Ex.Application.Backup;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Application.Exceptions;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.DataTransfer;

/// <summary>
/// Owns the WebDAV wire protocol and transport safety rules. Higher-level
/// synchronization and conflict decisions remain in <see cref="WebDavSyncService"/>.
/// </summary>
internal sealed class WebDavClient(
    TimeProvider timeProvider,
    ILogger<WebDavClient> logger)
{
    private const int MaximumAttempts = 3;
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan NoProgressTimeout = TimeSpan.FromSeconds(60);
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromMinutes(30);

    public HttpClient CreateHttpClient(
        string username,
        string? password,
        WebDavTlsVerifyMode tlsVerifyMode)
    {
        var handler = CreateHandler(username, password, tlsVerifyMode);
        var client = new HttpClient(handler, disposeHandler: true)
        {
            Timeout = RequestTimeout
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("IGoLibrary-Ex-WebDAV/1.0");
        return client;
    }

    internal static SocketsHttpHandler CreateHandler(
        string username,
        string? password,
        WebDavTlsVerifyMode tlsVerifyMode)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            ConnectTimeout = ConnectTimeout,
            AutomaticDecompression = DecompressionMethods.All,
            UseCookies = false,
            Credentials = string.IsNullOrEmpty(username)
                ? null
                : new NetworkCredential(username, password),
            PreAuthenticate = false
        };

        if (tlsVerifyMode == WebDavTlsVerifyMode.Skip)
        {
            handler.SslOptions.RemoteCertificateValidationCallback =
                static (_, _, _, _) => true;
        }

        return handler;
    }

    public async Task ProbeWriteAsync(
        HttpClient client,
        Uri fileUri,
        CancellationToken cancellationToken)
    {
        const string probePayload = "IGoLibrary-Ex";
        var probeUri = BuildSiblingUri(fileUri, $".igolibrary-ex-probe-{Guid.NewGuid():N}.tmp");
        var probeWritten = false;
        try
        {
            using (var put = new HttpRequestMessage(HttpMethod.Put, probeUri))
            {
                put.Headers.TryAddWithoutValidation("If-None-Match", "*");
                put.Content = new ByteArrayContent(Encoding.UTF8.GetBytes(probePayload));
                using var response = await SendAsync(client, put, cancellationToken);
                EnsureSuccess(response, "WebDAV 写入探测失败");
                probeWritten = true;
            }

            using var get = new HttpRequestMessage(HttpMethod.Get, probeUri);
            using var getResponse = await SendAsync(client, get, cancellationToken);
            EnsureSuccess(getResponse, "WebDAV 读取探测失败");
            var downloadedPayload = await ReadTextWithLimitAsync(
                getResponse.Content,
                Encoding.UTF8.GetByteCount(probePayload),
                "WebDAV 读取探测响应",
                cancellationToken);
            if (!string.Equals(downloadedPayload, probePayload, StringComparison.Ordinal))
            {
                throw new InvalidDataException("WebDAV 读取探测返回的内容与写入内容不一致");
            }
        }
        catch
        {
            if (probeWritten)
            {
                await DeleteProbeAsync(
                    client,
                    probeUri,
                    cancellationToken,
                    suppressFailure: true);
            }

            throw;
        }

        await DeleteProbeAsync(
            client,
            probeUri,
            cancellationToken,
            suppressFailure: false);
    }

    private async Task DeleteProbeAsync(
        HttpClient client,
        Uri probeUri,
        CancellationToken cancellationToken,
        bool suppressFailure)
    {
        try
        {
            using var delete = new HttpRequestMessage(HttpMethod.Delete, probeUri);
            using var response = await SendAsync(client, delete, cancellationToken);
            if (response.StatusCode is not (HttpStatusCode.NoContent or HttpStatusCode.OK or HttpStatusCode.NotFound))
            {
                EnsureSuccess(response, "WebDAV 探测文件清理失败");
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "WebDAV connection probe cleanup failed.");
            if (!suppressFailure)
            {
                throw;
            }
        }
    }

    public async Task EnsureCollectionsAsync(
        HttpClient client,
        Uri endpointUri,
        string remotePath,
        CancellationToken cancellationToken)
    {
        var segments = remotePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = endpointUri;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            current = new Uri(current, Uri.EscapeDataString(segments[index]) + "/");
            var metadata = await GetMetadataAsync(client, current, cancellationToken);
            if (metadata.Exists)
            {
                continue;
            }

            using var request = new HttpRequestMessage(new HttpMethod("MKCOL"), current);
            using var response = await SendAsync(client, request, cancellationToken);
            if (response.StatusCode is not (HttpStatusCode.Created or HttpStatusCode.MethodNotAllowed))
            {
                EnsureSuccess(response, "创建 WebDAV 目录失败");
            }
        }
    }

    public async Task<WebDavRemoteMetadata> GetMetadataAsync(
        HttpClient client,
        Uri uri,
        CancellationToken cancellationToken)
    {
        using (var propResponse = await SendWithRetryAsync(
                   client,
                   () => CreatePropFindRequest(uri),
                   cancellationToken,
                   HttpCompletionOption.ResponseHeadersRead,
                   "PROPFIND"))
        {
            if (propResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return new WebDavRemoteMetadata(false, null, null, null);
            }

            if ((int)propResponse.StatusCode == 207)
            {
                var body = await ReadTextWithLimitAsync(
                    propResponse.Content,
                    1024 * 1024,
                    "WebDAV 属性响应",
                    cancellationToken);
                return ParsePropFindMetadata(body, uri);
            }

            if (propResponse.StatusCode is not (HttpStatusCode.MethodNotAllowed or HttpStatusCode.NotImplemented))
            {
                EnsureSuccess(propResponse, "读取 WebDAV 属性失败");
            }
        }

        using var response = await SendWithRetryAsync(
            client,
            () => new HttpRequestMessage(HttpMethod.Head, uri),
            cancellationToken,
            HttpCompletionOption.ResponseHeadersRead,
            "HEAD");
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return new WebDavRemoteMetadata(false, null, null, null);
        }

        EnsureSuccess(response, "读取 WebDAV 资源信息失败");
        return ReadMetadata(response, exists: true);
    }

    public async Task PutFileAsync(
        HttpClient client,
        Uri uri,
        string filePath,
        WebDavRemoteMetadata before,
        CancellationToken cancellationToken)
    {
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using var stream = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                1024 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var request = new HttpRequestMessage(HttpMethod.Put, uri);
            if (!before.Exists)
            {
                request.Headers.TryAddWithoutValidation("If-None-Match", "*");
            }
            else if (TryGetStrongETag(before.ETag, out var strongETag))
            {
                request.Headers.TryAddWithoutValidation("If-Match", strongETag);
            }
            else if (before.LastModified is { } lastModified)
            {
                request.Headers.IfUnmodifiedSince = lastModified;
            }
            else
            {
                throw new BackupSyncConflictException(
                    "WebDAV 服务未提供 ETag 或 Last-Modified，无法对远端覆盖执行竞态保护");
            }

            request.Content = new StreamContent(stream);
            request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            request.Content.Headers.ContentLength = stream.Length;
            try
            {
                using var response = await SendAsync(
                    client,
                    request,
                    cancellationToken,
                    HttpCompletionOption.ResponseHeadersRead);
                if (response.StatusCode == HttpStatusCode.PreconditionFailed)
                {
                    throw new BackupSyncConflictException("上传期间远端备份发生变化，本次覆盖已取消");
                }

                if (IsRetryableStatus(response.StatusCode) && attempt < MaximumAttempts)
                {
                    var delay = GetRetryDelay(response, attempt);
                    logger.LogWarning(
                        "Retrying WebDAV PUT after HTTP {StatusCode}. Attempt={Attempt}, DelayMs={DelayMs}.",
                        (int)response.StatusCode,
                        attempt,
                        delay.TotalMilliseconds);
                    await Task.Delay(delay, timeProvider, cancellationToken);
                    continue;
                }

                EnsureSuccess(response, "上传 WebDAV 备份失败");
                return;
            }
            catch (Exception ex) when (
                !cancellationToken.IsCancellationRequested &&
                IsRetryable(ex) &&
                attempt < MaximumAttempts)
            {
                lastFailure = ex;
                logger.LogWarning(ex, "Retrying WebDAV PUT. Attempt={Attempt}.", attempt);
                await Task.Delay(TimeSpan.FromSeconds(1 << (attempt - 1)), timeProvider, cancellationToken);
            }
        }

        throw lastFailure ?? new InvalidOperationException("上传 WebDAV 备份失败");
    }

    public async Task DownloadFileAsync(
        HttpClient client,
        Uri uri,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(
            client,
            () => new HttpRequestMessage(HttpMethod.Get, uri),
            cancellationToken,
            HttpCompletionOption.ResponseHeadersRead,
            "GET");
        EnsureSuccess(response, "下载 WebDAV 备份失败");
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            1024 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await CopyWithLimitAndTimeoutAsync(source, destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }

    public async Task<string> HashRemoteFileAsync(
        HttpClient client,
        Uri uri,
        CancellationToken cancellationToken)
    {
        using var response = await SendWithRetryAsync(
            client,
            () => new HttpRequestMessage(HttpMethod.Get, uri),
            cancellationToken,
            HttpCompletionOption.ResponseHeadersRead,
            "GET");
        EnsureSuccess(response, "校验 WebDAV 远端内容失败");
        if (response.Content.Headers.ContentLength > EncryptedBackupArchiveCodec.MaximumArchiveSize)
        {
            throw new InvalidDataException("WebDAV 远端备份超过 2 GiB 限制");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[1024 * 1024];
        long total = 0;
        while (true)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(NoProgressTimeout);
            int read;
            try
            {
                read = await source.ReadAsync(buffer, timeout.Token);
            }
            catch (OperationCanceledException ex) when (
                !cancellationToken.IsCancellationRequested &&
                timeout.IsCancellationRequested)
            {
                throw new TimeoutException("读取 WebDAV 远端备份超过 60 秒没有进度", ex);
            }
            if (read == 0)
            {
                return Convert.ToHexString(hash.GetHashAndReset());
            }

            total += read;
            if (total > EncryptedBackupArchiveCodec.MaximumArchiveSize)
            {
                throw new InvalidDataException("WebDAV 远端备份超过 2 GiB 限制");
            }

            hash.AppendData(buffer.AsSpan(0, read));
        }
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken cancellationToken)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
    }

    internal static Uri BuildFileUri(Uri endpointUri, string remotePath)
    {
        var current = endpointUri;
        var segments = remotePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < segments.Length; index++)
        {
            current = new Uri(current, Uri.EscapeDataString(segments[index]));
            if (index < segments.Length - 1)
            {
                current = new Uri(current.AbsoluteUri + "/");
            }
        }

        return current;
    }

    internal static bool HasSameOrigin(Uri source, Uri target)
        => string.Equals(source.Scheme, target.Scheme, StringComparison.OrdinalIgnoreCase) &&
           string.Equals(source.IdnHost, target.IdnHost, StringComparison.OrdinalIgnoreCase) &&
           source.Port == target.Port;

    internal static bool TryGetStrongETag(string? value, out string strongETag)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            EntityTagHeaderValue.TryParse(value, out var parsed) &&
            !parsed.IsWeak)
        {
            strongETag = parsed.ToString();
            return true;
        }

        strongETag = string.Empty;
        return false;
    }

    internal static WebDavRemoteMetadata ParsePropFindMetadata(string xml, Uri requestedUri)
    {
        ArgumentNullException.ThrowIfNull(requestedUri);
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = 1024 * 1024
        };
        using var stringReader = new StringReader(xml);
        using var reader = XmlReader.Create(stringReader, settings);
        var document = new XmlDocument { XmlResolver = null };
        document.Load(reader);
        var namespaceManager = new XmlNamespaceManager(document.NameTable);
        namespaceManager.AddNamespace("d", "DAV:");
        var responses = document.SelectNodes("/d:multistatus/d:response", namespaceManager)
                        ?? throw new InvalidDataException("WebDAV 属性响应缺少 response 元素");
        foreach (XmlNode response in responses)
        {
            var href = response.SelectSingleNode("d:href", namespaceManager)?.InnerText;
            if (!TryResolveResponseUri(requestedUri, href, out var responseUri) ||
                !IsSameResource(requestedUri, responseUri))
            {
                continue;
            }

            var responseStatus = ParseStatusCode(
                response.SelectSingleNode("d:status", namespaceManager)?.InnerText);
            if (responseStatus == (int)HttpStatusCode.NotFound)
            {
                return new WebDavRemoteMetadata(false, null, null, null);
            }

            string? lengthText = null;
            string? etagText = null;
            string? modifiedText = null;
            var propStats = response.SelectNodes("d:propstat", namespaceManager);
            if (propStats is not null)
            {
                foreach (XmlNode propStat in propStats)
                {
                    var statusCode = ParseStatusCode(
                        propStat.SelectSingleNode("d:status", namespaceManager)?.InnerText);
                    if (statusCode is < 200 or >= 300)
                    {
                        continue;
                    }

                    var properties = propStat.SelectSingleNode("d:prop", namespaceManager);
                    lengthText ??= properties?.SelectSingleNode("d:getcontentlength", namespaceManager)?.InnerText;
                    etagText ??= properties?.SelectSingleNode("d:getetag", namespaceManager)?.InnerText;
                    modifiedText ??= properties?.SelectSingleNode("d:getlastmodified", namespaceManager)?.InnerText;
                }
            }

            long? length = long.TryParse(
                lengthText,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var parsedLength) && parsedLength >= 0
                ? parsedLength
                : null;
            var etag = EntityTagHeaderValue.TryParse(etagText?.Trim(), out var parsedETag)
                ? parsedETag.ToString()
                : null;
            DateTimeOffset? modified = DateTimeOffset.TryParse(
                modifiedText,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AllowWhiteSpaces |
                System.Globalization.DateTimeStyles.AssumeUniversal |
                System.Globalization.DateTimeStyles.AdjustToUniversal,
                out var parsedModified)
                ? parsedModified
                : null;
            return new WebDavRemoteMetadata(true, length, etag, modified);
        }

        throw new InvalidDataException("WebDAV 属性响应未包含请求的目标资源");
    }

    private static bool TryResolveResponseUri(
        Uri requestedUri,
        string? href,
        out Uri responseUri)
    {
        if (!string.IsNullOrWhiteSpace(href) &&
            Uri.TryCreate(requestedUri, href.Trim(), out var parsed))
        {
            responseUri = parsed;
            return true;
        }

        responseUri = null!;
        return false;
    }

    private static bool IsSameResource(Uri requestedUri, Uri responseUri)
        => HasSameOrigin(requestedUri, responseUri) &&
           string.Equals(
               NormalizeResourcePath(requestedUri),
               NormalizeResourcePath(responseUri),
               StringComparison.Ordinal);

    private static string NormalizeResourcePath(Uri uri)
    {
        var pathAndQuery = uri.GetComponents(
            UriComponents.PathAndQuery,
            UriFormat.UriEscaped);
        return pathAndQuery.Length > 1
            ? pathAndQuery.TrimEnd('/')
            : pathAndQuery;
    }

    private static int? ParseStatusCode(string? statusLine)
    {
        if (string.IsNullOrWhiteSpace(statusLine))
        {
            return null;
        }

        var parts = statusLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 && int.TryParse(
            parts[1],
            System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture,
            out var statusCode)
            ? statusCode
            : null;
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpRequestMessage request,
        CancellationToken cancellationToken,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseHeadersRead)
    {
        HttpRequestMessage current = request;
        var clonedRequests = new List<HttpRequestMessage>();
        try
        {
            for (var redirectCount = 0; ; redirectCount++)
            {
                var response = await client.SendAsync(current, completionOption, cancellationToken);
                if (!IsRedirectStatus(response.StatusCode))
                {
                    return response;
                }

                if (redirectCount >= 5 || response.Headers.Location is null)
                {
                    response.Dispose();
                    throw new InvalidOperationException("WebDAV 重定向次数过多或缺少目标地址");
                }

                var sourceUri = current.RequestUri
                                ?? throw new InvalidOperationException("WebDAV 请求地址无效");
                var targetUri = response.Headers.Location.IsAbsoluteUri
                    ? response.Headers.Location
                    : new Uri(sourceUri, response.Headers.Location);
                response.Dispose();
                if (!HasSameOrigin(sourceUri, targetUri))
                {
                    throw new InvalidOperationException(
                        sourceUri.Scheme == Uri.UriSchemeHttps && targetUri.Scheme == Uri.UriSchemeHttp
                            ? "拒绝将 WebDAV HTTPS 请求降级重定向到 HTTP"
                            : "拒绝将 WebDAV 请求重定向到其他来源，以免泄露凭据");
                }

                current = await CloneRequestAsync(current, targetUri, cancellationToken);
                clonedRequests.Add(current);
            }
        }
        finally
        {
            foreach (var clone in clonedRequests)
            {
                clone.Dispose();
            }
        }
    }

    private async Task<HttpResponseMessage> SendWithRetryAsync(
        HttpClient client,
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken,
        HttpCompletionOption completionOption,
        string methodName)
    {
        Exception? lastFailure = null;
        for (var attempt = 1; attempt <= MaximumAttempts; attempt++)
        {
            using var request = requestFactory();
            try
            {
                var response = await SendAsync(client, request, cancellationToken, completionOption);
                if (!IsRetryableStatus(response.StatusCode) || attempt == MaximumAttempts)
                {
                    return response;
                }

                var delay = GetRetryDelay(response, attempt);
                response.Dispose();
                logger.LogWarning(
                    "Retrying WebDAV {Method} after a recoverable response. Attempt={Attempt}, DelayMs={DelayMs}.",
                    methodName,
                    attempt,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
            catch (Exception ex) when (
                !cancellationToken.IsCancellationRequested &&
                IsRetryable(ex) &&
                attempt < MaximumAttempts)
            {
                lastFailure = ex;
                var delay = TimeSpan.FromSeconds(1 << (attempt - 1));
                logger.LogWarning(
                    ex,
                    "Retrying WebDAV {Method} after a network failure. Attempt={Attempt}, DelayMs={DelayMs}.",
                    methodName,
                    attempt,
                    delay.TotalMilliseconds);
                await Task.Delay(delay, timeProvider, cancellationToken);
            }
        }

        throw lastFailure ?? new InvalidOperationException($"WebDAV {methodName} 请求失败");
    }

    private async Task CopyWithLimitAndTimeoutAsync(
        Stream source,
        Stream destination,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[1024 * 1024];
        long total = 0;
        while (true)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(NoProgressTimeout);
            int read;
            try
            {
                read = await source.ReadAsync(buffer, timeout.Token);
            }
            catch (OperationCanceledException ex) when (
                !cancellationToken.IsCancellationRequested &&
                timeout.IsCancellationRequested)
            {
                throw new TimeoutException("读取 WebDAV 响应超过 60 秒没有进度", ex);
            }
            if (read == 0)
            {
                return;
            }

            total += read;
            if (total > EncryptedBackupArchiveCodec.MaximumArchiveSize)
            {
                throw new InvalidDataException("WebDAV 远端备份超过 2 GiB 限制");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static WebDavRemoteMetadata ReadMetadata(HttpResponseMessage response, bool exists)
        => new(
            exists,
            response.Content.Headers.ContentLength,
            response.Headers.ETag?.ToString(),
            response.Content.Headers.LastModified);

    private static void EnsureSuccess(HttpResponseMessage response, string message)
    {
        if (response.IsSuccessStatusCode || (int)response.StatusCode == 207)
        {
            return;
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            throw new UnauthorizedAccessException($"{message}：服务器拒绝了账号或权限");
        }

        throw new HttpRequestException($"{message}（HTTP {(int)response.StatusCode}）", null, response.StatusCode);
    }

    private static Uri BuildSiblingUri(Uri fileUri, string fileName)
        => new(fileUri, Uri.EscapeDataString(fileName));

    private static bool IsRetryable(Exception exception)
        => exception is IOException or TimeoutException or OperationCanceledException ||
           exception is HttpRequestException { StatusCode: null or HttpStatusCode.RequestTimeout } ||
           exception is HttpRequestException { StatusCode: HttpStatusCode.TooManyRequests } ||
           exception is HttpRequestException { StatusCode: >= HttpStatusCode.InternalServerError };

    private static bool IsRetryableStatus(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests ||
           statusCode >= HttpStatusCode.InternalServerError;

    private TimeSpan GetRetryDelay(HttpResponseMessage response, int attempt)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            var remaining = date - timeProvider.GetUtcNow();
            if (remaining > TimeSpan.Zero)
            {
                return remaining;
            }
        }

        return TimeSpan.FromSeconds(1 << (attempt - 1));
    }

    private static bool IsRedirectStatus(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static async Task<HttpRequestMessage> CloneRequestAsync(
        HttpRequestMessage source,
        Uri target,
        CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(source.Method, target)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy
        };
        foreach (var header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (source.Content is not null)
        {
            var stream = await source.Content.ReadAsStreamAsync(cancellationToken);
            if (!stream.CanSeek)
            {
                clone.Dispose();
                throw new InvalidOperationException("WebDAV 服务重定向了不可重放的流式请求");
            }

            stream.Position = 0;
            clone.Content = new StreamContent(stream);
            foreach (var header in source.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private static async Task<string> ReadTextWithLimitAsync(
        HttpContent content,
        int maximumBytes,
        string responseName,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength > maximumBytes)
        {
            throw new InvalidDataException($"{responseName}超过安全大小限制");
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var memory = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(NoProgressTimeout);
            int read;
            try
            {
                read = await stream.ReadAsync(buffer, timeout.Token);
            }
            catch (OperationCanceledException ex) when (
                !cancellationToken.IsCancellationRequested &&
                timeout.IsCancellationRequested)
            {
                throw new TimeoutException($"读取{responseName}超过 60 秒没有进度", ex);
            }
            if (read == 0)
            {
                return Encoding.UTF8.GetString(memory.ToArray());
            }

            if (memory.Length + read > maximumBytes)
            {
                throw new InvalidDataException($"{responseName}超过安全大小限制");
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }

    private static HttpRequestMessage CreatePropFindRequest(Uri uri)
    {
        var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), uri);
        request.Headers.TryAddWithoutValidation("Depth", "0");
        request.Content = new StringContent(
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:getcontentlength/><d:getetag/><d:getlastmodified/></d:prop></d:propfind>",
            Encoding.UTF8,
            "application/xml");
        return request;
    }
}
