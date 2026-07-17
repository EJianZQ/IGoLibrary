namespace IGoLibrary.Ex.Desktop.Services;

internal static class CloudflaredFileSystemSafety
{
    internal static bool IsPathInside(string root, string candidate)
    {
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        var normalizedCandidate = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return normalizedCandidate.StartsWith(
            normalizedRoot + Path.DirectorySeparatorChar,
            comparison);
    }

    internal static void DeleteDirectorySafely(string root, string target)
        => DeleteEntrySafely(root, target);

    internal static void DeleteEntrySafely(string root, string target)
    {
        var entry = TryGetExistingEntry(target);
        if (entry is null)
        {
            return;
        }

        if (!IsPathInside(root, target))
        {
            throw new IOException($"拒绝清理受控目录之外的路径：{target}");
        }

        EnsureRootIsNotLink(root);
        EnsureParentsAreNotLinks(root, target);
        DeleteEntry(entry);
    }

    internal static bool EntryExists(string path)
        => TryGetExistingEntry(path) is not null;

    internal static bool IsDirectoryWithoutLinks(string path)
    {
        var entry = TryGetExistingEntry(path);
        return entry is DirectoryInfo && entry.Exists && !IsLink(entry);
    }

    internal static void EnsureRootIsNotLink(string root)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var rootInfo = TryGetExistingEntry(normalizedRoot);
        if (rootInfo is null)
        {
            return;
        }

        if (IsLink(rootInfo))
        {
            throw new IOException($"cloudflared 受控根目录不允许是符号链接或重解析点：{normalizedRoot}");
        }

        if (rootInfo is not DirectoryInfo)
        {
            throw new IOException($"cloudflared 受控根路径不是目录：{normalizedRoot}");
        }
    }

    internal static void EnsureNoLinksInExistingPath(string root, string target)
    {
        if (!IsPathInside(root, target))
        {
            throw new IOException($"目标路径不在受控目录内：{target}");
        }

        var normalizedRoot = Path.GetFullPath(root);
        var normalizedTarget = Path.GetFullPath(target);
        EnsureRootIsNotLink(normalizedRoot);

        var relative = Path.GetRelativePath(normalizedRoot, normalizedTarget);
        var current = normalizedRoot;
        foreach (var segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            var info = TryGetExistingEntry(current);
            if (info is null)
            {
                break;
            }

            if (IsLink(info))
            {
                throw new IOException($"cloudflared 受控路径中不允许符号链接或重解析点：{current}");
            }

            if (info is not DirectoryInfo)
            {
                throw new IOException($"cloudflared 受控路径中的目录位置被文件占用：{current}");
            }
        }
    }

    internal static void EnsureParentPathIsSafe(string root, string target)
    {
        if (!IsPathInside(root, target))
        {
            throw new IOException($"目标路径不在受控目录内：{target}");
        }

        EnsureRootIsNotLink(root);
        EnsureParentsAreNotLinks(root, target);
    }

    private static void EnsureParentsAreNotLinks(string root, string target)
    {
        var normalizedRoot = Path.GetFullPath(root);
        var normalizedTarget = Path.GetFullPath(target);
        var relative = Path.GetRelativePath(normalizedRoot, normalizedTarget);
        var segments = relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        var current = normalizedRoot;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            current = Path.Combine(current, segments[index]);
            var info = TryGetExistingEntry(current);
            if (info is null)
            {
                break;
            }

            if (IsLink(info))
            {
                throw new IOException($"cloudflared 受控路径中不允许符号链接或重解析点：{current}");
            }

            if (info is not DirectoryInfo)
            {
                throw new IOException($"cloudflared 受控路径中的父目录位置被文件占用：{current}");
            }
        }
    }

    private static void DeleteEntry(FileSystemInfo entry)
    {
        entry.Refresh();
        if (IsLink(entry))
        {
            entry.Delete();
            return;
        }

        if (!entry.Exists)
        {
            return;
        }

        if (entry is DirectoryInfo directory)
        {
            foreach (var child in directory.EnumerateFileSystemInfos())
            {
                DeleteEntry(child);
            }

            directory.Delete();
            return;
        }

        entry.Delete();
    }

    internal static bool IsLink(FileSystemInfo info)
    {
        try
        {
            if (!string.IsNullOrEmpty(info.LinkTarget))
            {
                return true;
            }

            return info.Exists &&
                   (info.Attributes & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception exception) when (
            exception is FileNotFoundException or DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static FileSystemInfo? TryGetExistingEntry(string path)
    {
        var directory = new DirectoryInfo(path);
        directory.Refresh();
        if (directory.Exists)
        {
            return directory;
        }

        var file = new FileInfo(path);
        file.Refresh();
        if (file.Exists)
        {
            return file;
        }

        if (IsLink(file))
        {
            try
            {
                return (file.Attributes & FileAttributes.Directory) != 0
                    ? directory
                    : file;
            }
            catch (Exception exception) when (
                exception is FileNotFoundException or DirectoryNotFoundException)
            {
                return file;
            }
        }

        return IsLink(directory) ? directory : null;
    }
}
