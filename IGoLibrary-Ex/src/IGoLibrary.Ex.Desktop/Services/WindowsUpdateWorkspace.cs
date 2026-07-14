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
    private static readonly HashSet<string> VerifiedCacheEntryNames = new(
        ["package.zip", "staging", "verified-cache.json"],
        StringComparer.OrdinalIgnoreCase);
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
                var cache = ReadValidVerifiedCacheLayout(directory, transactionId);
                var now = _timeProvider.GetUtcNow();
                if (cache.VerifiedAtUtc > now + TimeSpan.FromMinutes(5) ||
                    now - cache.VerifiedAtUtc >= VerifiedCacheRetention)
                {
                    _logger.LogWarning(
                        "已验签更新缓存元数据无效或已过期，正在清理。事务={TransactionId}。",
                        transactionId);
                    TryDelete(directory, "缓存元数据无效或过期");
                    continue;
                }

                if (!IsPureVerifiedCacheDirectory(directory))
                {
                    _logger.LogWarning(
                        "验签缓存仍包含未完成的交接产物，暂不复用并等待安全清理。事务={TransactionId}。",
                        transactionId);
                    continue;
                }

                if (!string.Equals(cache.TargetVersion, targetVersion, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(cache.PackageDigest, asset.Digest, StringComparison.OrdinalIgnoreCase) ||
                    cache.PackageSize != asset.Size)
                {
                    _logger.LogInformation(
                        "跳过与当前 Release 资产不匹配的有效验签缓存。事务={TransactionId}，缓存版本={CachedVersion}，目标版本={TargetVersion}。",
                        transactionId,
                        cache.TargetVersion,
                        targetVersion);
                    continue;
                }

                var workspace = new WindowsUpdateWorkspace(
                    transactionId,
                    directory,
                    isVerifiedCache: true);
                await VerifyArchiveDigestAsync(workspace.ArchivePath, asset, cancellationToken);
                var manifest = UpdatePackageValidator.LoadAndValidateManifest(
                    Path.Combine(workspace.StagingDirectory, UpdateProtocol.ManifestFileName),
                    targetVersion);
                await UpdatePackageValidator.ValidateUpdatePayloadDirectoryAsync(
                    workspace.StagingDirectory,
                    manifest,
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

    public bool TryRestoreVerifiedCache(
        WindowsUpdateWorkspace workspace,
        string reason)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!workspace.IsVerifiedCache)
        {
            _logger.LogWarning(
                "拒绝把未验签工作区恢复为验签缓存。事务={TransactionId}，原因={RestoreReason}。",
                workspace.TransactionId,
                reason);
            return false;
        }

        try
        {
            RestoreVerifiedCacheDirectory(
                UpdatesRoot,
                workspace.TransactionDirectory,
                workspace.TransactionId);
            _logger.LogInformation(
                "更新交接未完成，已恢复可复用的纯验签缓存。事务={TransactionId}，原因={RestoreReason}。",
                workspace.TransactionId,
                reason);
            return true;
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "更新交接产物暂时无法清理，保留事务并将在下次启动重试。事务={TransactionId}，原因={RestoreReason}。",
                workspace.TransactionId,
                reason);
            return false;
        }
    }

    public bool TryDelete(WindowsUpdateWorkspace workspace, string reason)
    {
        return TryDelete(workspace.TransactionDirectory, reason);
    }

    public bool TryDelete(string transactionDirectory, string reason)
    {
        try
        {
            var transactionId = Path.GetFileName(Path.GetFullPath(transactionDirectory));
            var target = EnsureSafeTransactionDirectory(
                UpdatesRoot,
                transactionDirectory,
                transactionId);

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

    internal static VerifiedUpdateCache ReadValidVerifiedCacheLayout(
        string transactionDirectory,
        string transactionId)
    {
        UpdatePathSafety.RejectReparsePoint(transactionDirectory);
        var markerPath = Path.Combine(transactionDirectory, "verified-cache.json");
        var archivePath = Path.Combine(transactionDirectory, "package.zip");
        var stagingDirectory = Path.Combine(transactionDirectory, "staging");
        var manifestPath = Path.Combine(stagingDirectory, UpdateProtocol.ManifestFileName);
        if (!File.Exists(markerPath) ||
            !File.Exists(archivePath) ||
            !Directory.Exists(stagingDirectory) ||
            !File.Exists(manifestPath))
        {
            throw new InvalidDataException("验签缓存缺少必要文件");
        }

        UpdatePathSafety.RejectReparsePoint(markerPath);
        UpdatePathSafety.RejectReparsePoint(archivePath);
        UpdatePathSafety.RejectReparsePoint(stagingDirectory);
        UpdatePathSafety.RejectReparsePoint(manifestPath);
        var cache = UpdateJsonFile.Read<VerifiedUpdateCache>(markerPath);
        if (!VerifiedUpdateCache.IsStructurallyValid(cache, transactionId))
        {
            throw new InvalidDataException("验签缓存元数据无效");
        }

        return cache;
    }

    internal static void RestoreVerifiedCacheDirectory(
        string updatesRoot,
        string transactionDirectory,
        string transactionId)
    {
        var target = EnsureSafeTransactionDirectory(
            updatesRoot,
            transactionDirectory,
            transactionId);
        ReadValidVerifiedCacheLayout(target, transactionId);

        var extraEntries = Directory.EnumerateFileSystemEntries(target)
            .Where(entry => !VerifiedCacheEntryNames.Contains(Path.GetFileName(entry)))
            .ToArray();
        foreach (var entry in extraEntries.Where(static entry =>
                     !string.Equals(
                         Path.GetFileName(entry),
                         "request.json",
                         StringComparison.OrdinalIgnoreCase)))
        {
            DeleteEntryWithoutFollowingReparsePoints(entry);
        }

        foreach (var requestPath in extraEntries.Where(static entry =>
                     string.Equals(
                         Path.GetFileName(entry),
                         "request.json",
                         StringComparison.OrdinalIgnoreCase)))
        {
            DeleteEntryWithoutFollowingReparsePoints(requestPath);
        }

        if (!IsPureVerifiedCacheDirectory(target))
        {
            throw new IOException("更新交接产物未能全部清理");
        }
    }

    internal static bool IsPureVerifiedCacheDirectory(string transactionDirectory)
    {
        return Directory.EnumerateFileSystemEntries(transactionDirectory)
            .All(entry => VerifiedCacheEntryNames.Contains(Path.GetFileName(entry)));
    }

    private static void DeleteEntryWithoutFollowingReparsePoints(string entry)
    {
        var attributes = File.GetAttributes(entry);
        if ((attributes & FileAttributes.Directory) != 0)
        {
            DeleteDirectoryWithoutFollowingReparsePoints(entry);
        }
        else
        {
            File.Delete(entry);
        }
    }

    private static string EnsureSafeTransactionDirectory(
        string updatesRoot,
        string transactionDirectory,
        string transactionId)
    {
        var root = Path.GetFullPath(updatesRoot);
        var target = Path.GetFullPath(transactionDirectory);
        if (!string.Equals(Path.GetDirectoryName(target), root, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetFileName(target), transactionId, StringComparison.Ordinal) ||
            !Guid.TryParseExact(transactionId, "N", out _))
        {
            throw new InvalidDataException("更新事务目录不在允许的 updates 根目录内");
        }

        if (Directory.Exists(root))
        {
            UpdatePathSafety.RejectReparsePoint(root);
        }

        return target;
    }
}
