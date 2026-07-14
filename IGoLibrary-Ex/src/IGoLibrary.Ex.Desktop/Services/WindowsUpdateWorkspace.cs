using System.Security.Cryptography;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Updater.Core;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed class WindowsUpdateWorkspace(
    string transactionId,
    string transactionDirectory,
    bool isVerifiedCache)
{
    public string TransactionId { get; } = transactionId;

    public string TransactionDirectory { get; } = transactionDirectory;

    public string ArchivePath { get; } = Path.Combine(transactionDirectory, "package.zip");

    public string PartialArchivePath => ArchivePath + ".partial";

    public string StagingDirectory { get; } = Path.Combine(transactionDirectory, "staging");

    public bool IsVerifiedCache { get; private set; } = isVerifiedCache;

    public void MarkVerified() => IsVerifiedCache = true;
}

internal sealed class WindowsUpdateWorkspaceManager
{
    internal static readonly TimeSpan VerifiedCacheRetention = TimeSpan.FromDays(7);
    private readonly ILogger<WindowsUpdateWorkspaceManager> _logger;
    private readonly string _updatesRoot;
    private readonly TimeProvider _timeProvider;

    public WindowsUpdateWorkspaceManager(
        ILogger<WindowsUpdateWorkspaceManager> logger)
        : this(logger, GetUpdatesRoot(), TimeProvider.System)
    {
    }

    internal WindowsUpdateWorkspaceManager(
        ILogger<WindowsUpdateWorkspaceManager> logger,
        string updatesRoot,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(updatesRoot);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _logger = logger;
        _updatesRoot = Path.GetFullPath(updatesRoot);
        _timeProvider = timeProvider;
    }

    public string UpdatesRoot => _updatesRoot;

    public void EnsureRoot()
    {
        Directory.CreateDirectory(UpdatesRoot);
        UpdatePathSafety.RejectReparsePoint(UpdatesRoot);
    }

    public WindowsUpdateWorkspace Create()
    {
        EnsureRoot();
        var transactionId = Guid.NewGuid().ToString("N");
        var transactionDirectory = Path.Combine(UpdatesRoot, transactionId);
        Directory.CreateDirectory(transactionDirectory);
        return new WindowsUpdateWorkspace(
            transactionId,
            transactionDirectory,
            isVerifiedCache: false);
    }

    public async Task<WindowsUpdateWorkspace?> TryFindVerifiedAsync(
        ReleaseAssetInfo asset,
        string targetVersion,
        CancellationToken cancellationToken)
    {
        EnsureRoot();
        foreach (var directory in Directory.EnumerateDirectories(UpdatesRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var transactionId = Path.GetFileName(directory);
            if (!Guid.TryParseExact(transactionId, "N", out _) ||
                File.Exists(Path.Combine(directory, "request.json")))
            {
                continue;
            }

            var markerPath = Path.Combine(directory, "verified-cache.json");
            if (!File.Exists(markerPath))
            {
                continue;
            }

            try
            {
                UpdatePathSafety.RejectReparsePoint(directory);
                UpdatePathSafety.RejectReparsePoint(markerPath);
                var cache = UpdateJsonFile.Read<VerifiedUpdateCache>(markerPath);
                var now = _timeProvider.GetUtcNow();
                if (!VerifiedUpdateCache.IsStructurallyValid(cache, transactionId) ||
                    !string.Equals(cache.TargetVersion, targetVersion, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(cache.PackageDigest, asset.Digest, StringComparison.OrdinalIgnoreCase) ||
                    cache.PackageSize != asset.Size ||
                    cache.VerifiedAtUtc > now + TimeSpan.FromMinutes(5) ||
                    now - cache.VerifiedAtUtc >= VerifiedCacheRetention)
                {
                    _logger.LogWarning(
                        "已验签更新缓存元数据无效或已过期，正在清理。事务={TransactionId}。",
                        transactionId);
                    TryDelete(directory, "缓存元数据无效或过期");
                    continue;
                }

                var workspace = new WindowsUpdateWorkspace(
                    transactionId,
                    directory,
                    isVerifiedCache: true);
                UpdatePathSafety.RejectReparsePoint(workspace.ArchivePath);
                UpdatePathSafety.RejectReparsePoint(workspace.StagingDirectory);
                await VerifyArchiveDigestAsync(workspace.ArchivePath, asset, cancellationToken);
                var manifest = UpdatePackageValidator.LoadAndValidateManifest(
                    Path.Combine(workspace.StagingDirectory, UpdateProtocol.ManifestFileName),
                    targetVersion);
                await UpdatePackageValidator.ValidateDirectoryAsync(
                    workspace.StagingDirectory,
                    manifest,
                    allowAdditionalFiles: false,
                    cancellationToken);
                _logger.LogInformation(
                    "已验签更新缓存复核通过。事务={TransactionId}，目标版本={TargetVersion}。",
                    transactionId,
                    targetVersion);
                return workspace;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "已验签更新缓存复核失败，正在清理。事务={TransactionId}。",
                    transactionId);
                TryDelete(directory, "缓存复核失败");
            }
        }

        return null;
    }

    public void WriteVerifiedMarker(
        WindowsUpdateWorkspace workspace,
        string targetVersion,
        ReleaseAssetInfo asset)
    {
        UpdateJsonFile.WriteAtomic(
            Path.Combine(workspace.TransactionDirectory, "verified-cache.json"),
            new VerifiedUpdateCache(
                UpdateProtocol.SchemaVersion,
                workspace.TransactionId,
                targetVersion,
                asset.Digest,
                asset.Size,
                _timeProvider.GetUtcNow()));
        workspace.MarkVerified();
    }

    public bool TryDelete(WindowsUpdateWorkspace workspace, string reason)
    {
        return TryDelete(workspace.TransactionDirectory, reason);
    }

    public bool TryDelete(string transactionDirectory, string reason)
    {
        try
        {
            var root = Path.GetFullPath(UpdatesRoot);
            var target = Path.GetFullPath(transactionDirectory);
            var parent = Path.GetDirectoryName(target);
            var transactionId = Path.GetFileName(target);
            if (!string.Equals(parent, root, StringComparison.OrdinalIgnoreCase) ||
                !Guid.TryParseExact(transactionId, "N", out _))
            {
                throw new InvalidDataException("更新事务目录不在允许的 updates 根目录内");
            }

            if (!Directory.Exists(target))
            {
                return true;
            }

            DeleteDirectoryWithoutFollowingReparsePoints(target);

            _logger.LogInformation(
                "已清理更新事务目录。事务={TransactionId}，原因={CleanupReason}。",
                transactionId,
                reason);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "更新事务目录暂时无法清理，将在下次启动重试。原因={CleanupReason}。",
                reason);
            return false;
        }
    }

    public static async Task VerifyArchiveDigestAsync(
        string archivePath,
        ReleaseAssetInfo asset,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(archivePath);
        if (!info.Exists || info.Length != asset.Size)
        {
            throw new InvalidDataException("更新包实际大小与 GitHub 声明不一致");
        }

        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("更新包不能是符号链接或其他重解析点");
        }

        await using var stream = new FileStream(
            archivePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, cancellationToken));
        if (!string.Equals(digest, asset.Digest[7..], StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("更新包 SHA-256 校验失败");
        }
    }

    internal static string GetUpdatesRoot()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            UpdateProtocol.ProductName,
            "updates");
    }

    internal static void DeleteDirectoryWithoutFollowingReparsePoints(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return;
        }

        var attributes = File.GetAttributes(directory);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            Directory.Delete(directory, recursive: false);
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
        {
            var entryAttributes = File.GetAttributes(entry);
            if ((entryAttributes & FileAttributes.Directory) != 0)
            {
                DeleteDirectoryWithoutFollowingReparsePoints(entry);
            }
            else
            {
                File.Delete(entry);
            }
        }

        Directory.Delete(directory, recursive: false);
    }
}
