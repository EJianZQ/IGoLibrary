using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace IGoLibrary.Ex.Updater.Core;

public static class UpdatePackageValidator
{
    public const long MaximumArchiveBytes = 512L * 1024 * 1024;
    public const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
    public const int MaximumEntryCount = 10_000;

    public static UpdatePackageManifest LoadAndValidateManifest(
        string manifestPath,
        string? expectedVersion = null)
    {
        UpdatePackageManifest manifest;
        try
        {
            manifest = UpdateJsonFile.Read(manifestPath, UpdateJsonTypeInfo.PackageManifest);
        }
        catch (FileNotFoundException exception)
        {
            throw new InvalidDataException(
                $"缺少必需的更新清单文件：{UpdateProtocol.ManifestFileName}",
                exception);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"更新清单 JSON 格式无效：{UpdateProtocol.ManifestFileName}",
                exception);
        }

        ValidateManifest(manifest, expectedVersion);
        return manifest;
    }

    public static void ValidateManifest(
        UpdatePackageManifest manifest,
        string? expectedVersion = null)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.SchemaVersion != UpdateProtocol.SchemaVersion ||
            !string.Equals(manifest.Product, UpdateProtocol.ProductName, StringComparison.Ordinal) ||
            !string.Equals(manifest.Runtime, UpdateProtocol.WindowsX64Runtime, StringComparison.Ordinal) ||
            !string.Equals(
                manifest.EntryExecutable,
                UpdateProtocol.EntryExecutableName,
                StringComparison.OrdinalIgnoreCase) ||
            !StableUpdateVersion.TryParseCanonical(manifest.Version, out _) ||
            (expectedVersion is not null &&
             !string.Equals(manifest.Version, expectedVersion, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("更新包 manifest 的产品、版本或运行时不匹配");
        }

        if (manifest.Files.Count == 0 || manifest.Files.Count > MaximumEntryCount)
        {
            throw new InvalidDataException("更新包 manifest 的文件数量无效");
        }

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            var normalized = UpdatePathSafety.NormalizeRelativePath(file.Path);
            if (!paths.Add(normalized) || file.Size < 0 || !IsSha256(file.Sha256))
            {
                throw new InvalidDataException($"更新包 manifest 文件项无效：{file.Path}");
            }

            if (string.Equals(
                    normalized,
                    UpdateProtocol.ManifestFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException("manifest 不能把自身列入文件清单");
            }
        }

        if (!paths.Contains(UpdateProtocol.EntryExecutableName) ||
            !paths.Contains(UpdateProtocol.UpdaterExecutableName))
        {
            throw new InvalidDataException("更新包缺少主程序或独立 updater");
        }

        if (!paths.Contains(UpdateProtocol.PortableMarkerFileName))
        {
            throw new InvalidDataException("更新包缺少 Windows 绿色版标记");
        }
    }

    public static void ValidateUpdatePayloadManifest(UpdatePackageManifest manifest)
    {
        ValidateManifest(manifest);
        var preservedPath = manifest.Files
            .Select(static file => UpdatePathSafety.NormalizeRelativePath(file.Path))
            .FirstOrDefault(UpdateProtocol.IsPreservedInstallationPath);
        if (preservedPath is not null)
        {
            throw new InvalidDataException($"更新包不得管理保留目录：{preservedPath}");
        }
    }

    public static async Task<UpdatePackageManifest> ExtractAndValidateAsync(
        string archivePath,
        string destinationDirectory,
        string expectedVersion,
        IProgress<(long Completed, long Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var archiveInfo = new FileInfo(archivePath);
        if (!archiveInfo.Exists || archiveInfo.Length <= 0 || archiveInfo.Length > MaximumArchiveBytes)
        {
            throw new InvalidDataException("Windows 更新压缩包大小无效");
        }

        if (Directory.Exists(destinationDirectory) &&
            Directory.EnumerateFileSystemEntries(destinationDirectory).Any())
        {
            throw new IOException("更新暂存目录不是空目录");
        }

        Directory.CreateDirectory(destinationDirectory);
        UpdatePathSafety.RejectReparsePoint(destinationDirectory);

        using var archive = ZipFile.OpenRead(archivePath);
        if (archive.Entries.Count > MaximumEntryCount)
        {
            throw new InvalidDataException("更新压缩包文件数量超过安全上限");
        }

        var entryPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new List<(ZipArchiveEntry Entry, string RelativePath)>();
        long totalExpandedBytes = 0;
        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var trimmedName = entry.FullName.TrimEnd('/', '\\');
            if (trimmedName.Length == 0)
            {
                continue;
            }

            var relativePath = UpdatePathSafety.NormalizeRelativePath(trimmedName);
            if (!entryPaths.Add(relativePath))
            {
                throw new InvalidDataException($"更新压缩包包含重复路径：{relativePath}");
            }

            if (UpdateProtocol.IsPreservedInstallationPath(relativePath))
            {
                throw new InvalidDataException($"更新压缩包不得包含保留目录：{relativePath}");
            }

            RejectLinkEntry(entry);
            if (entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\'))
            {
                continue;
            }

            checked
            {
                totalExpandedBytes += entry.Length;
            }

            if (totalExpandedBytes > MaximumExpandedBytes)
            {
                throw new InvalidDataException("更新压缩包解压体积超过安全上限");
            }

            files.Add((entry, relativePath));
        }

        long completed = 0;
        foreach (var (entry, relativePath) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destinationPath = UpdatePathSafety.GetSafeChildPath(destinationDirectory, relativePath);
            var parent = Path.GetDirectoryName(destinationPath)
                         ?? throw new InvalidDataException($"无法确定解压目标目录：{relativePath}");
            Directory.CreateDirectory(parent);

            await using (var source = entry.Open())
            await using (var target = new FileStream(
                             destinationPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 128 * 1024,
                             FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                await source.CopyToAsync(target, 128 * 1024, cancellationToken);
            }

            completed += entry.Length;
            progress?.Report((completed, totalExpandedBytes));
        }

        var manifestPath = Path.Combine(destinationDirectory, UpdateProtocol.ManifestFileName);
        var manifest = LoadAndValidateManifest(manifestPath, expectedVersion);
        ValidateUpdatePayloadManifest(manifest);
        await ValidateDirectoryAsync(
            destinationDirectory,
            manifest,
            allowAdditionalFiles: false,
            cancellationToken);
        return manifest;
    }

    public static async Task ValidateArchiveDigestAsync(
        string archivePath,
        string expectedDigest,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        if (!expectedDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
            !IsSha256(expectedDigest[7..]))
        {
            throw new InvalidDataException("更新包摘要格式无效");
        }

        var info = new FileInfo(archivePath);
        if (!info.Exists || info.Length <= 0 || info.Length > MaximumArchiveBytes)
        {
            throw new InvalidDataException("Windows 更新压缩包大小无效");
        }

        await using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await ValidateArchiveDigestAsync(stream, expectedDigest, cancellationToken);
    }

    public static async Task ValidateArchiveDigestAsync(
        Stream archiveStream,
        string expectedDigest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        if (!expectedDigest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) ||
            !IsSha256(expectedDigest[7..]))
        {
            throw new InvalidDataException("更新包摘要格式无效");
        }

        if (!archiveStream.CanRead)
        {
            throw new InvalidDataException("更新包流不可读");
        }

        if (archiveStream.CanSeek)
        {
            archiveStream.Position = 0;
        }

        var actual = Convert.ToHexString(await SHA256.HashDataAsync(archiveStream, cancellationToken));
        if (!string.Equals(actual, expectedDigest[7..], StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新包 SHA-256 校验失败");
        }

        if (archiveStream.CanSeek)
        {
            archiveStream.Position = 0;
        }
    }

    public static async Task ValidateDirectoryAsync(
        string rootDirectory,
        UpdatePackageManifest manifest,
        bool allowAdditionalFiles,
        CancellationToken cancellationToken = default)
    {
        await ValidateDirectoryCoreAsync(
            rootDirectory,
            manifest,
            allowAdditionalFiles,
            ignorePreservedPaths: false,
            cancellationToken);
    }

    public static async Task ValidateInstalledDirectoryAsync(
        string rootDirectory,
        UpdatePackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        await ValidateDirectoryCoreAsync(
            rootDirectory,
            manifest,
            allowAdditionalFiles: true,
            ignorePreservedPaths: true,
            cancellationToken);
    }

    public static async Task ValidateUpdatePayloadDirectoryAsync(
        string rootDirectory,
        UpdatePackageManifest manifest,
        CancellationToken cancellationToken = default)
    {
        ValidateUpdatePayloadManifest(manifest);
        var root = Path.GetFullPath(rootDirectory);
        UpdatePathSafety.RejectReparsePoint(root);
        var preservedRootEntry = Directory.EnumerateFileSystemEntries(root)
            .FirstOrDefault(path => string.Equals(
                Path.GetFileName(path),
                UpdateProtocol.PreservedToolsDirectoryName,
                StringComparison.OrdinalIgnoreCase));
        if (preservedRootEntry is not null)
        {
            throw new InvalidDataException(
                $"更新包不得包含保留目录：{Path.GetFileName(preservedRootEntry)}");
        }

        foreach (var filePath in EnumerateFilesWithoutReparsePoints(root))
        {
            var relativePath = UpdatePathSafety.NormalizeRelativePath(
                Path.GetRelativePath(root, filePath));
            if (UpdateProtocol.IsPreservedInstallationPath(relativePath))
            {
                throw new InvalidDataException($"更新包不得包含保留目录：{relativePath}");
            }
        }

        await ValidateDirectoryCoreAsync(
            rootDirectory,
            manifest,
            allowAdditionalFiles: false,
            ignorePreservedPaths: false,
            cancellationToken);
    }

    private static async Task ValidateDirectoryCoreAsync(
        string rootDirectory,
        UpdatePackageManifest manifest,
        bool allowAdditionalFiles,
        bool ignorePreservedPaths,
        CancellationToken cancellationToken)
    {
        ValidateManifest(manifest);
        var root = Path.GetFullPath(rootDirectory);
        UpdatePathSafety.RejectReparsePoint(root);

        var expected = manifest.Files
            .Select(static file => UpdatePathSafety.NormalizeRelativePath(file.Path))
            .Where(path => !ignorePreservedPaths || !UpdateProtocol.IsPreservedInstallationPath(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        expected.Add(UpdateProtocol.ManifestFileName);

        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in EnumerateFilesWithoutReparsePoints(root))
        {
            var relativePath = UpdatePathSafety.NormalizeRelativePath(Path.GetRelativePath(root, filePath));
            if (!actual.Add(relativePath))
            {
                throw new InvalidDataException($"目录包含大小写冲突文件：{relativePath}");
            }
        }

        if (!expected.IsSubsetOf(actual) || (!allowAdditionalFiles && !actual.SetEquals(expected)))
        {
            throw new InvalidDataException("更新包文件集合与 manifest 不一致");
        }

        foreach (var file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ignorePreservedPaths && UpdateProtocol.IsPreservedInstallationPath(file.Path))
            {
                continue;
            }

            var path = UpdatePathSafety.GetSafeChildPath(root, file.Path);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != file.Size)
            {
                throw new InvalidDataException($"更新文件大小不匹配：{file.Path}");
            }

            await using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 128 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"更新文件 SHA-256 不匹配：{file.Path}");
            }
        }

        ValidatePortableMarker(root);
    }

    public static void ValidatePortableMarker(string rootDirectory)
    {
        var markerPath = UpdatePathSafety.GetSafeChildPath(
            rootDirectory,
            UpdateProtocol.PortableMarkerFileName);
        if (!File.Exists(markerPath))
        {
            throw new InvalidDataException("当前目录缺少 Windows 绿色版标记");
        }

        var expected = Encoding.UTF8.GetBytes(UpdateProtocol.PortableMarkerContent);
        var actual = File.ReadAllBytes(markerPath);
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidDataException("Windows 绿色版标记内容无效");
        }
    }

    public static async Task ValidateFileAsync(
        string path,
        long expectedSize,
        string expectedSha256,
        CancellationToken cancellationToken = default)
    {
        if (!IsSha256(expectedSha256))
        {
            throw new InvalidDataException("文件摘要格式无效");
        }

        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expectedSize)
        {
            throw new InvalidDataException($"文件大小不匹配：{Path.GetFileName(path)}");
        }

        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(hash, expectedSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"文件 SHA-256 不匹配：{Path.GetFileName(path)}");
        }
    }

    public static bool IsSha256(string? value)
    {
        return value is { Length: 64 } && value.All(Uri.IsHexDigit);
    }

    internal static IEnumerable<string> EnumerateFilesWithoutReparsePoints(string rootDirectory)
    {
        var pending = new Stack<string>();
        pending.Push(rootDirectory);
        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException($"目录包含不受支持的链接或联接：{entry}");
                }

                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                }
                else
                {
                    yield return entry;
                }
            }
        }
    }

    private static void RejectLinkEntry(ZipArchiveEntry entry)
    {
        const int unixFileTypeMask = 0xF000;
        const int unixSymbolicLink = 0xA000;
        var unixMode = (entry.ExternalAttributes >> 16) & unixFileTypeMask;
        var windowsAttributes = (FileAttributes)(entry.ExternalAttributes & 0xFFFF);
        if (unixMode == unixSymbolicLink ||
            (windowsAttributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException($"更新压缩包包含符号链接：{entry.FullName}");
        }
    }
}
