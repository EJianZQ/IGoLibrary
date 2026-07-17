using System.Formats.Tar;
using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal interface ICloudflaredExtractor
{
    Task PrepareExecutableAsync(
        CloudflaredAssetDescriptor asset,
        string payloadPath,
        string destinationPath,
        CancellationToken cancellationToken = default);
}

internal sealed class CloudflaredExtractor(
    ILogger<CloudflaredExtractor> logger) : ICloudflaredExtractor
{
    public async Task PrepareExecutableAsync(
        CloudflaredAssetDescriptor asset,
        string payloadPath,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        if (asset.ArchiveType == "binary")
        {
            await CopyFileAsync(payloadPath, destinationPath, cancellationToken);
            logger.LogInformation("cloudflared 原始二进制已复制到安装 staging。");
            return;
        }

        if (asset.ArchiveType != "tgz")
        {
            throw new InvalidDataException($"不支持的 cloudflared 下载格式：{asset.ArchiveType}");
        }

        await using var archive = new FileStream(
            payloadPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var gzip = new GZipStream(archive, CompressionMode.Decompress, leaveOpen: false);
        using var reader = new TarReader(gzip, leaveOpen: false);
        var entry = reader.GetNextEntry(copyData: false)
                    ?? throw new InvalidDataException("cloudflared 压缩包为空");
        if (entry.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile) ||
            !string.Equals(entry.Name, "cloudflared", StringComparison.Ordinal) ||
            entry.DataStream is null)
        {
            throw new InvalidDataException("cloudflared 压缩包必须只包含一个名为 cloudflared 的普通文件");
        }

        if (entry.Length != asset.ExecutableSize)
        {
            throw new InvalidDataException(
                $"cloudflared 压缩包中的可执行文件大小无效：预期 {asset.ExecutableSize} 字节，实际 {entry.Length} 字节");
        }

        logger.LogInformation(
            "cloudflared TGZ 条目验证通过。名称={EntryName}，类型={EntryType}，长度={EntryLength}。",
            entry.Name,
            entry.EntryType,
            entry.Length);

        await using (var destination = new FileStream(
                         destinationPath,
                         FileMode.CreateNew,
                         FileAccess.Write,
                         FileShare.None,
                         128 * 1024,
                         FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            await entry.DataStream.CopyToAsync(destination, cancellationToken);
            await destination.FlushAsync(cancellationToken);
        }

        if (new FileInfo(destinationPath).Length != asset.ExecutableSize)
        {
            throw new InvalidDataException("cloudflared 压缩包条目在解压过程中提前结束");
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (reader.GetNextEntry(copyData: false) is not null)
        {
            throw new InvalidDataException("cloudflared 压缩包包含意外的额外条目");
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(
                destinationPath,
                UnixFileMode.UserRead |
                UnixFileMode.UserWrite |
                UnixFileMode.UserExecute |
                UnixFileMode.GroupRead |
                UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead |
                UnixFileMode.OtherExecute);
            logger.LogInformation("已为 cloudflared 设置 Unix 可执行权限。");
        }
    }

    private static async Task CopyFileAsync(
        string sourcePath,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        await using var source = new FileStream(
            sourcePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await using var destination = new FileStream(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await source.CopyToAsync(destination, cancellationToken);
        await destination.FlushAsync(cancellationToken);
    }
}
