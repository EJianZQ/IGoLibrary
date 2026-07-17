using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

internal interface ICloudflaredDownloadWorkspace
{
    string CurrentDirectory { get; }

    void Initialize();

    void Cleanup(string reason);

    void CleanupAndRenew(string reason);

    long GetPreservedBytes(CloudflaredAssetDescriptor asset);
}

internal sealed class CloudflaredDownloadWorkspace : ICloudflaredDownloadWorkspace
{
    private readonly ICloudflaredPathProvider _paths;
    private readonly ILogger<CloudflaredDownloadWorkspace> _logger;
    private readonly Action<string, string> _deleteEntry;
    private readonly HashSet<string> _pendingCleanup = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private string? _currentDirectory;

    public CloudflaredDownloadWorkspace(
        ICloudflaredPathProvider paths,
        ILogger<CloudflaredDownloadWorkspace> logger)
        : this(paths, logger, CloudflaredFileSystemSafety.DeleteEntrySafely)
    {
    }

    internal CloudflaredDownloadWorkspace(
        ICloudflaredPathProvider paths,
        ILogger<CloudflaredDownloadWorkspace> logger,
        Action<string, string> deleteEntry)
    {
        _paths = paths;
        _logger = logger;
        _deleteEntry = deleteEntry;
    }

    public string CurrentDirectory => _currentDirectory
        ?? throw new InvalidOperationException("cloudflared 下载工作区尚未初始化");

    public void Initialize()
    {
        CloudflaredFileSystemSafety.EnsureRootIsNotLink(_paths.DownloadWorkspaceRoot);
        Directory.CreateDirectory(_paths.DownloadWorkspaceRoot);
        CloudflaredFileSystemSafety.EnsureRootIsNotLink(_paths.DownloadWorkspaceRoot);
        CleanupPreviousProcesses();
        CreateCurrentDirectory();
    }

    public void Cleanup(string reason)
    {
        var directory = _currentDirectory;
        _currentDirectory = null;
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _pendingCleanup.Add(directory);
        }

        RetryPendingCleanup(reason);
    }

    public void CleanupAndRenew(string reason)
    {
        Cleanup(reason);
        try
        {
            CreateCurrentDirectory();
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "创建新的 cloudflared 下载工作区失败；不会覆盖已完成操作的结果，将在下次下载时重试。");
        }
    }

    public long GetPreservedBytes(CloudflaredAssetDescriptor asset)
    {
        var directory = _currentDirectory;
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return 0;
        }

        var payload = Path.Combine(directory, asset.FileName);
        var partial = payload + ".partial";
        return File.Exists(payload)
            ? new FileInfo(payload).Length
            : File.Exists(partial)
                ? new FileInfo(partial).Length
                : 0;
    }

    private void CleanupPreviousProcesses()
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(_paths.DownloadWorkspaceRoot).ToArray())
        {
            _pendingCleanup.Add(entry);
        }

        RetryPendingCleanup("启动时清理上次进程遗留");
    }

    private void CreateCurrentDirectory()
    {
        _currentDirectory = Path.Combine(
            _paths.DownloadWorkspaceRoot,
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_currentDirectory);
    }

    private void RetryPendingCleanup(string reason)
    {
        foreach (var directory in _pendingCleanup.ToArray())
        {
            try
            {
                _deleteEntry(_paths.DownloadWorkspaceRoot, directory);
                _pendingCleanup.Remove(directory);
                _logger.LogInformation(
                    "cloudflared 下载工作区已清理。原因={CleanupReason}，目录={Directory}。",
                    reason,
                    directory);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(
                    exception,
                    "cloudflared 下载工作区清理失败，已保留路径供本进程退出或下次启动重试。原因={CleanupReason}，目录={Directory}。",
                    reason,
                    directory);
            }
        }
    }
}
