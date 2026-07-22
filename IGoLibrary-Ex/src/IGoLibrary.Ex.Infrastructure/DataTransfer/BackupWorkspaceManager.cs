using IGoLibrary.Ex.Application.Abstractions;

namespace IGoLibrary.Ex.Infrastructure.DataTransfer;

internal sealed class BackupWorkspaceManager
{
    private const string RootDirectoryName = ".backup-sync";
    private readonly string _root;

    public BackupWorkspaceManager(StorageLocations locations)
    {
        _root = Path.GetFullPath(Path.Combine(locations.DataDirectory, RootDirectoryName));
    }

    public string Root => _root;

    public string Create(string category, string id)
    {
        if (string.IsNullOrWhiteSpace(category) ||
            !Guid.TryParseExact(id, "N", out _))
        {
            throw new ArgumentException("备份工作区标识无效", nameof(id));
        }

        Directory.CreateDirectory(_root);
        var categoryRoot = Path.GetFullPath(Path.Combine(_root, category));
        EnsureChildPath(_root, categoryRoot);
        Directory.CreateDirectory(categoryRoot);
        var workspace = Path.GetFullPath(Path.Combine(categoryRoot, id));
        EnsureChildPath(categoryRoot, workspace);
        Directory.CreateDirectory(workspace);
        RejectReparsePoint(workspace);
        return workspace;
    }

    public string GetTransactionDirectory(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
        {
            throw new ArgumentException("恢复事务标识无效", nameof(id));
        }

        var path = Path.GetFullPath(Path.Combine(_root, "restore", id));
        EnsureChildPath(Path.Combine(_root, "restore"), path);
        return path;
    }

    public IEnumerable<string> EnumerateTransactionDirectories()
    {
        var restoreRoot = Path.Combine(_root, "restore");
        return Directory.Exists(restoreRoot)
            ? Directory.EnumerateDirectories(restoreRoot, "*", SearchOption.TopDirectoryOnly)
            : [];
    }

    public IEnumerable<string> EnumerateTransientDirectories()
    {
        foreach (var category in new[] { "export", "preview", "webdav", "fingerprint" })
        {
            var categoryRoot = Path.Combine(_root, category);
            if (!Directory.Exists(categoryRoot))
            {
                continue;
            }

            foreach (var directory in Directory.EnumerateDirectories(
                         categoryRoot,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                yield return directory;
            }
        }
    }

    public void Delete(string path)
    {
        var fullPath = Path.GetFullPath(path);
        EnsureChildPath(_root, fullPath);
        if (!Directory.Exists(fullPath))
        {
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(fullPath, "*", SearchOption.AllDirectories))
        {
            RejectReparsePoint(entry);
        }

        RejectReparsePoint(fullPath);
        Directory.Delete(fullPath, recursive: true);
    }

    public void CleanupPreviews(TimeSpan maximumAge, DateTimeOffset now)
    {
        var previewRoot = Path.Combine(_root, "preview");
        if (!Directory.Exists(previewRoot))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(previewRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var info = new DirectoryInfo(directory);
            if (now - info.LastWriteTimeUtc > maximumAge)
            {
                Delete(directory);
            }
        }
    }

    private static void EnsureChildPath(string root, string candidate)
    {
        var normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(candidate).StartsWith(
                normalizedRoot,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new InvalidOperationException("备份工作区路径越界");
        }
    }

    private static void RejectReparsePoint(string path)
    {
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("备份工作区不能包含符号链接或重解析点");
        }
    }
}
