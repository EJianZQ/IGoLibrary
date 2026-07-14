using System.Buffers;
using System.Security.Cryptography;
using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Infrastructure.Updates;

public sealed class GitHubReleaseAssetDownloader : IReleaseAssetDownloader
{
    private const long MaximumDownloadBytes = 512L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly TimeSpan _noProgressTimeout;

    public GitHubReleaseAssetDownloader(
        HttpClient httpClient,
        TimeSpan? noProgressTimeout = null)
    {
        _httpClient = httpClient;
        _noProgressTimeout = noProgressTimeout ?? TimeSpan.FromSeconds(60);
        if (_noProgressTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(noProgressTimeout));
        }
    }

    public async Task DownloadAsync(
        ReleaseAssetInfo asset,
        string destinationPath,
        IProgress<ReleaseAssetDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);
        ValidateAsset(asset);

        var destination = Path.GetFullPath(destinationPath);
        var destinationDirectory = Path.GetDirectoryName(destination)
                                   ?? throw new InvalidOperationException("无法确定更新下载目录");
        Directory.CreateDirectory(destinationDirectory);
        var partialPath = destination + ".partial";
        TryDelete(partialPath);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, asset.BrowserDownloadUrl);
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();

            if (response.RequestMessage?.RequestUri is not { Scheme: "https" })
            {
                throw new InvalidDataException("GitHub 下载被重定向到非 HTTPS 地址");
            }

            if (response.Content.Headers.ContentLength is not { } contentLength)
            {
                throw new InvalidDataException("GitHub 下载响应缺少 Content-Length");
            }

            if (contentLength != asset.Size)
            {
                throw new InvalidDataException(
                    $"GitHub 资产大小不一致：预期 {asset.Size}，响应 {contentLength}");
            }

            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var destinationStream = new FileStream(
                partialPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var incrementalHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(128 * 1024);
            long downloaded = 0;
            try
            {
                while (true)
                {
                    using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    readTimeout.CancelAfter(_noProgressTimeout);
                    int count;
                    try
                    {
                        count = await source.ReadAsync(buffer.AsMemory(), readTimeout.Token);
                    }
                    catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        throw new TimeoutException("下载更新包时连续 60 秒没有收到数据", ex);
                    }

                    if (count == 0)
                    {
                        break;
                    }

                    downloaded += count;
                    if (downloaded > asset.Size || downloaded > MaximumDownloadBytes)
                    {
                        throw new InvalidDataException("下载数据超过 GitHub 资产声明大小");
                    }

                    incrementalHash.AppendData(buffer, 0, count);
                    await destinationStream.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    progress?.Report(new ReleaseAssetDownloadProgress(downloaded, asset.Size));
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            await destinationStream.FlushAsync(cancellationToken);
            if (downloaded != asset.Size)
            {
                throw new InvalidDataException(
                    $"更新包下载不完整：预期 {asset.Size} 字节，实际 {downloaded} 字节");
            }

            var actualHash = Convert.ToHexString(incrementalHash.GetHashAndReset());
            var expectedHash = asset.Digest[7..];
            if (!string.Equals(actualHash, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("更新包 SHA-256 与 GitHub Release 不一致");
            }

            await destinationStream.DisposeAsync();
            TryDelete(destination);
            File.Move(partialPath, destination);
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }
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

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
