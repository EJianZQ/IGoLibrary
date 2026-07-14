using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using IGoLibrary.Ex.Application.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Infrastructure.Updates;

public sealed class GitHubReleaseAssetDownloader : IReleaseAssetDownloader
{
    private const long MaximumDownloadBytes = 512L * 1024 * 1024;
    private const int BufferSize = 128 * 1024;
    private static readonly TimeSpan MaximumRetryAfter = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan[] DefaultRetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(4)
    ];

    private readonly HttpClient _httpClient;
    private readonly ILogger<GitHubReleaseAssetDownloader> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _noProgressTimeout;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;

    public GitHubReleaseAssetDownloader(
        HttpClient httpClient,
        ILogger<GitHubReleaseAssetDownloader> logger)
        : this(
            httpClient,
            logger,
            TimeProvider.System)
    {
    }

    internal GitHubReleaseAssetDownloader(
        HttpClient httpClient,
        TimeSpan? noProgressTimeout = null)
        : this(
            httpClient,
            NullLogger<GitHubReleaseAssetDownloader>.Instance,
            TimeProvider.System,
            noProgressTimeout)
    {
    }

    internal GitHubReleaseAssetDownloader(
        HttpClient httpClient,
        ILogger<GitHubReleaseAssetDownloader> logger,
        TimeProvider timeProvider,
        TimeSpan? noProgressTimeout = null,
        IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _timeProvider = timeProvider;
        _noProgressTimeout = noProgressTimeout ?? TimeSpan.FromSeconds(60);
        _retryDelays = retryDelays ?? DefaultRetryDelays;

        if (_noProgressTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(noProgressTimeout));
        }

        if (_retryDelays.Count != 3 || _retryDelays.Any(static delay => delay < TimeSpan.Zero))
        {
            throw new ArgumentException("自动下载重试必须配置三个非负退避时间", nameof(retryDelays));
        }
    }

    public async Task DownloadAsync(
        ReleaseAssetInfo asset,
        string destinationPath,
        IProgress<ReleaseAssetDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default,
        IReleaseAssetDownloadPauseSource? pauseSource = null)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ValidateAsset(asset);

        var destination = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(destination)
                                   ?? throw new InvalidOperationException("无法确定更新下载目录");
        Directory.CreateDirectory(destinationDirectory);
        var partialPath = destination + ".partial";
        var consecutiveFailures = 0;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (pauseSource?.IsPaused == true)
                {
                    await WaitForResumeAsync(
                        pauseSource,
                        progress,
                        GetPartialLength(partialPath),
                        asset.Size,
                        cancellationToken);
                    continue;
                }

                var offset = GetPartialLength(partialPath);
                if (offset > asset.Size)
                {
                    _logger.LogWarning(
                        "更新包片段超过声明大小，正在清理并重新下载。资源={AssetName}，片段大小={PartialSize}，声明大小={AssetSize}。",
                        asset.Name,
                        offset,
                        asset.Size);
                    DeleteForRestart(partialPath);
                    offset = 0;
                }

                if (offset == asset.Size)
                {
                    await VerifyAndPublishAsync(
                        asset,
                        partialPath,
                        destination,
                        progress,
                        cancellationToken);
                    return;
                }

                progress?.Report(new ReleaseAssetDownloadProgress(
                    offset,
                    asset.Size,
                    ReleaseAssetDownloadState.Connecting));
                var pauseToken = pauseSource?.PauseToken ?? CancellationToken.None;
                try
                {
                    var result = await DownloadSegmentAsync(
                        asset,
                        partialPath,
                        offset,
                        progress,
                        cancellationToken,
                        pauseToken);
                    if (result.RestartRequired)
                    {
                        consecutiveFailures = 0;
                        continue;
                    }

                    if (result.BytesWritten > 0)
                    {
                        consecutiveFailures = 0;
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException) when (pauseToken.IsCancellationRequested)
                {
                    if (pauseSource is not null)
                    {
                        await WaitForResumeAsync(
                            pauseSource,
                            progress,
                            GetPartialLength(partialPath),
                            asset.Size,
                            cancellationToken);
                    }

                    continue;
                }
                catch (RecoverableDownloadException exception)
                {
                    var preservedBytes = GetPartialLength(partialPath);
                    consecutiveFailures = preservedBytes > offset
                        ? 1
                        : consecutiveFailures + 1;
                    if (consecutiveFailures > _retryDelays.Count)
                    {
                        _logger.LogWarning(
                            exception,
                            "更新包自动续传次数已耗尽，等待用户继续。资源={AssetName}，保留字节={PreservedBytes}。",
                            asset.Name,
                            preservedBytes);
                        throw new ReleaseAssetDownloadInterruptedException(
                            exception.Message,
                            preservedBytes,
                            exception);
                    }

                    var retryDelay = GetRetryDelay(
                        _retryDelays[consecutiveFailures - 1],
                        exception.RetryAfter);
                    _logger.LogWarning(
                        exception,
                        "更新包下载中断，将自动续传。资源={AssetName}，保留字节={PreservedBytes}，重试={RetryAttempt}/{RetryLimit}，等待={RetryDelayMs}ms。",
                        asset.Name,
                        preservedBytes,
                        consecutiveFailures,
                        _retryDelays.Count,
                        retryDelay.TotalMilliseconds);
                    progress?.Report(new ReleaseAssetDownloadProgress(
                        preservedBytes,
                        asset.Size,
                        ReleaseAssetDownloadState.Retrying,
                        consecutiveFailures,
                        retryDelay));
                    await WaitForRetryAsync(
                        retryDelay,
                        pauseSource,
                        progress,
                        preservedBytes,
                        asset.Size,
                        cancellationToken);
                }

                if (GetPartialLength(partialPath) == asset.Size)
                {
                    await VerifyAndPublishAsync(
                        asset,
                        partialPath,
                        destination,
                        progress,
                        cancellationToken);
                    return;
                }
            }
        }
        catch (ReleaseAssetDownloadInterruptedException)
        {
            throw;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDelete(partialPath, "用户取消下载");
            throw;
        }
        catch
        {
            TryDelete(partialPath, "下载无法安全续传");
            throw;
        }
    }

    private async Task<DownloadSegmentResult> DownloadSegmentAsync(
        ReleaseAssetInfo asset,
        string partialPath,
        long requestedOffset,
        IProgress<ReleaseAssetDownloadProgress>? progress,
        CancellationToken cancellationToken,
        CancellationToken pauseToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.BrowserDownloadUrl);
        request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
        if (requestedOffset > 0)
        {
            request.Headers.Range = new RangeHeaderValue(requestedOffset, null);
        }

        using var transferCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            pauseToken);
        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                transferCancellation.Token);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested || pauseToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException exception)
        {
            throw new RecoverableDownloadException("连接 GitHub 下载地址超时", innerException: exception);
        }
        catch (HttpRequestException exception)
        {
            throw new RecoverableDownloadException("连接 GitHub 下载地址失败", innerException: exception);
        }

        using (response)
        {
            EnsureSecureFinalUri(response);
            EnsureIdentityEncoding(response);
            if (IsRetryableStatusCode(response.StatusCode))
            {
                throw new RecoverableDownloadException(
                    $"GitHub 下载暂时不可用（HTTP {(int)response.StatusCode}）",
                    GetRetryAfter(response));
            }

            if (requestedOffset > 0 && response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
            {
                var currentLength = GetPartialLength(partialPath);
                if (currentLength == asset.Size)
                {
                    return new DownloadSegmentResult(0, RestartRequired: false);
                }

                _logger.LogWarning(
                    "GitHub 拒绝更新包 Range 请求，正在清理片段并从零重试。资源={AssetName}，请求偏移={RequestedOffset}。",
                    asset.Name,
                    requestedOffset);
                DeleteForRestart(partialPath);
                progress?.Report(new ReleaseAssetDownloadProgress(
                    0,
                    asset.Size,
                    ReleaseAssetDownloadState.Restarting));
                return new DownloadSegmentResult(0, RestartRequired: true);
            }

            var append = requestedOffset > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            long writeOffset;
            long expectedResponseBytes;
            if (append)
            {
                (writeOffset, expectedResponseBytes) = ValidatePartialResponse(
                    response,
                    requestedOffset,
                    asset.Size);
                _logger.LogInformation(
                    "正在续传更新包。资源={AssetName}，偏移={ResumeOffset}，区间字节={SegmentBytes}。",
                    asset.Name,
                    writeOffset,
                    expectedResponseBytes);
            }
            else if (response.StatusCode == HttpStatusCode.OK)
            {
                if (requestedOffset > 0)
                {
                    _logger.LogWarning(
                        "GitHub 未接受更新包 Range 请求，正在丢弃旧片段并从零下载。资源={AssetName}，旧片段={PartialBytes}。",
                        asset.Name,
                        requestedOffset);
                    progress?.Report(new ReleaseAssetDownloadProgress(
                        0,
                        asset.Size,
                        ReleaseAssetDownloadState.Restarting));
                    DeleteForRestart(partialPath);
                }

                writeOffset = 0;
                expectedResponseBytes = ValidateFullResponse(response, asset.Size);
            }
            else
            {
                throw new InvalidDataException(
                    $"GitHub 下载返回不受支持的 HTTP 状态：{(int)response.StatusCode}");
            }

            return await CopyResponseAsync(
                response,
                partialPath,
                append,
                writeOffset,
                expectedResponseBytes,
                asset.Size,
                progress,
                cancellationToken,
                pauseToken);
        }
    }

    private async Task<DownloadSegmentResult> CopyResponseAsync(
        HttpResponseMessage response,
        string partialPath,
        bool append,
        long writeOffset,
        long expectedResponseBytes,
        long totalBytes,
        IProgress<ReleaseAssetDownloadProgress>? progress,
        CancellationToken cancellationToken,
        CancellationToken pauseToken)
    {
        Stream source;
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                pauseToken);
            source = await response.Content.ReadAsStreamAsync(linked.Token);
        }
        catch (OperationCanceledException) when (
            cancellationToken.IsCancellationRequested || pauseToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException or HttpRequestException)
        {
            throw new RecoverableDownloadException("打开 GitHub 下载响应流失败", innerException: exception);
        }

        await using (source)
        await using (var destination = new FileStream(
                         partialPath,
                         append ? FileMode.Open : FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         BufferSize,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            if (append)
            {
                if (destination.Length != writeOffset)
                {
                    throw new InvalidDataException("更新包片段大小在续传前发生变化");
                }

                destination.Position = writeOffset;
            }

            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            long responseBytes = 0;
            try
            {
                while (responseBytes < expectedResponseBytes)
                {
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        pauseToken);
                    readTimeout.CancelAfter(_noProgressTimeout);
                    int count;
                    try
                    {
                        var remaining = Math.Min(
                            buffer.Length,
                            expectedResponseBytes - responseBytes);
                        count = await source.ReadAsync(
                            buffer.AsMemory(0, checked((int)remaining)),
                            readTimeout.Token);
                    }
                    catch (OperationCanceledException) when (
                        cancellationToken.IsCancellationRequested || pauseToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (OperationCanceledException exception)
                    {
                        throw new RecoverableDownloadException(
                            $"下载更新包时连续 {_noProgressTimeout.TotalSeconds:F0} 秒没有收到数据",
                            innerException: new TimeoutException("下载无进度超时", exception));
                    }
                    catch (Exception exception) when (exception is IOException or HttpRequestException)
                    {
                        throw new RecoverableDownloadException("读取 GitHub 下载响应时连接中断", innerException: exception);
                    }

                    if (count == 0)
                    {
                        throw new RecoverableDownloadException("更新包下载响应提前结束");
                    }

                    responseBytes += count;
                    var downloaded = writeOffset + responseBytes;
                    if (downloaded > totalBytes || downloaded > MaximumDownloadBytes)
                    {
                        throw new InvalidDataException("下载数据超过 GitHub 资产声明大小");
                    }

                    using var writeCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken,
                        pauseToken);
                    await destination.WriteAsync(
                        buffer.AsMemory(0, count),
                        writeCancellation.Token);
                    progress?.Report(new ReleaseAssetDownloadProgress(
                        downloaded,
                        totalBytes,
                        ReleaseAssetDownloadState.Downloading));
                }

                await destination.FlushAsync(cancellationToken);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return new DownloadSegmentResult(responseBytes, RestartRequired: false);
        }
    }

    private async Task VerifyAndPublishAsync(
        ReleaseAssetInfo asset,
        string partialPath,
        string destination,
        IProgress<ReleaseAssetDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        progress?.Report(new ReleaseAssetDownloadProgress(
            asset.Size,
            asset.Size,
            ReleaseAssetDownloadState.Verifying));
        await using var stream = new FileStream(
            partialPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != asset.Size)
        {
            throw new InvalidDataException(
                $"更新包下载不完整：预期 {asset.Size} 字节，实际 {stream.Length} 字节");
        }

        var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        var expectedHash = asset.Digest[7..];
        if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新包 SHA-256 与 GitHub Release 不一致");
        }

        await stream.DisposeAsync();
        File.Move(partialPath, destination, overwrite: true);
        _logger.LogInformation(
            "更新包下载及 SHA-256 校验完成。资源={AssetName}，大小={AssetSize}。",
            asset.Name,
            asset.Size);
    }

    private async Task WaitForRetryAsync(
        TimeSpan delay,
        IReleaseAssetDownloadPauseSource? pauseSource,
        IProgress<ReleaseAssetDownloadProgress>? progress,
        long downloadedBytes,
        long totalBytes,
        CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        var pauseToken = pauseSource?.PauseToken ?? CancellationToken.None;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            pauseToken);
        try
        {
            await Task.Delay(delay, _timeProvider, linked.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (pauseToken.IsCancellationRequested)
        {
            if (pauseSource is not null)
            {
                await WaitForResumeAsync(
                    pauseSource,
                    progress,
                    downloadedBytes,
                    totalBytes,
                    cancellationToken);
            }
        }
    }

    private async Task WaitForResumeAsync(
        IReleaseAssetDownloadPauseSource pauseSource,
        IProgress<ReleaseAssetDownloadProgress>? progress,
        long downloadedBytes,
        long totalBytes,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "更新包下载已暂停并保留进度。已下载={DownloadedBytes}，总大小={TotalBytes}。",
            downloadedBytes,
            totalBytes);
        progress?.Report(new ReleaseAssetDownloadProgress(
            downloadedBytes,
            totalBytes,
            ReleaseAssetDownloadState.Paused));
        await pauseSource.WaitWhilePausedAsync(cancellationToken);
        _logger.LogInformation(
            "正在继续更新包下载。续传偏移={ResumeOffset}，总大小={TotalBytes}。",
            downloadedBytes,
            totalBytes);
    }

    private static (long Offset, long Length) ValidatePartialResponse(
        HttpResponseMessage response,
        long requestedOffset,
        long assetSize)
    {
        var range = response.Content.Headers.ContentRange;
        if (range is null ||
            !string.Equals(range.Unit, "bytes", StringComparison.OrdinalIgnoreCase) ||
            range.From != requestedOffset ||
            range.To is not { } end ||
            end < requestedOffset ||
            range.Length != assetSize)
        {
            throw new InvalidDataException("GitHub 续传响应的 Content-Range 无效");
        }

        var rangeLength = checked(end - requestedOffset + 1);
        if (response.Content.Headers.ContentLength != rangeLength)
        {
            throw new InvalidDataException("GitHub 续传响应长度与 Content-Range 不一致");
        }

        return (requestedOffset, rangeLength);
    }

    private static long ValidateFullResponse(HttpResponseMessage response, long assetSize)
    {
        if (response.Content.Headers.ContentLength is not { } contentLength)
        {
            throw new InvalidDataException("GitHub 下载响应缺少 Content-Length");
        }

        if (contentLength != assetSize)
        {
            throw new InvalidDataException(
                $"GitHub 资产大小不一致：预期 {assetSize}，响应 {contentLength}");
        }

        return contentLength;
    }

    private static void EnsureSecureFinalUri(HttpResponseMessage response)
    {
        if (response.RequestMessage?.RequestUri is not { Scheme: "https" })
        {
            throw new InvalidDataException("GitHub 下载被重定向到非 HTTPS 地址");
        }
    }

    private static void EnsureIdentityEncoding(HttpResponseMessage response)
    {
        if (response.Content.Headers.ContentEncoding.Any(encoding =>
                !string.Equals(encoding, "identity", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("GitHub 下载响应使用了不支持的内容编码");
        }
    }

    private static bool IsRetryableStatusCode(HttpStatusCode statusCode)
    {
        var value = (int)statusCode;
        return value is 408 or 425 or 429 || value >= 500;
    }

    private TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        if (retryAfter?.Date is { } date)
        {
            return date - _timeProvider.GetUtcNow();
        }

        return null;
    }

    private static TimeSpan GetRetryDelay(TimeSpan fallback, TimeSpan? retryAfter)
    {
        var requested = retryAfter is { } value && value >= TimeSpan.Zero
            ? value
            : fallback;
        return requested <= TimeSpan.Zero
            ? TimeSpan.Zero
            : requested > MaximumRetryAfter
                ? MaximumRetryAfter
                : requested;
    }

    private static long GetPartialLength(string path)
    {
        return File.Exists(path) ? new FileInfo(path).Length : 0;
    }

    private static void ValidateAsset(ReleaseAssetInfo asset)
    {
        if (asset.Size <= 0 || asset.Size > MaximumDownloadBytes ||
            asset.BrowserDownloadUrl.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(asset.BrowserDownloadUrl.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !asset.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
            asset.Digest.Length != 71 ||
            !asset.Digest[7..].All(Uri.IsHexDigit))
        {
            throw new InvalidDataException("GitHub Release 资产元数据无效");
        }
    }

    private static void DeleteForRestart(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            throw new IOException("无法清理不可续传的更新包片段", exception);
        }
    }

    private void TryDelete(string path, string reason)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "更新包片段暂时无法清理，将由更新工作区或下次启动重试。原因={CleanupReason}。",
                reason);
        }
    }

    private sealed class RecoverableDownloadException(
        string message,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : IOException(message, innerException)
    {
        public TimeSpan? RetryAfter { get; } = retryAfter;
    }

    private readonly record struct DownloadSegmentResult(
        long BytesWritten,
        bool RestartRequired);
}
