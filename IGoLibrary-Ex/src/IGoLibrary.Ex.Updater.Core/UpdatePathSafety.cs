using System.Text.RegularExpressions;

namespace IGoLibrary.Ex.Updater.Core;

public static partial class UpdatePathSafety
{
    private static readonly char[] ExplicitInvalidFileNameChars =
        ['<', '>', ':', '"', '|', '?', '*'];

    private static readonly HashSet<string> WindowsReservedNames = new(
        [
            "CON", "PRN", "AUX", "NUL",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
        ],
        StringComparer.OrdinalIgnoreCase);

    public static string NormalizeRelativePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var normalized = path.Replace('\\', '/').Trim();
        if (normalized.Length == 0 ||
            normalized.StartsWith('/') ||
            DrivePrefixRegex().IsMatch(normalized))
        {
            throw new InvalidDataException($"更新包包含绝对路径：{path}");
        }

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            throw new InvalidDataException($"更新包路径为空：{path}");
        }

        foreach (var segment in segments)
        {
            ValidatePathSegment(segment, path);
        }

        return string.Join('/', segments);
    }

    public static string GetSafeChildPath(string rootDirectory, string relativePath)
    {
        var root = EnsureNotFileSystemRoot(rootDirectory, allowExistingRoot: true);
        var normalizedRelativePath = NormalizeRelativePath(relativePath);
        var candidate = Path.GetFullPath(
            Path.Combine(root, normalizedRelativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = EnsureTrailingSeparator(root);
        if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"更新包路径超出目标目录：{relativePath}");
        }

        return candidate;
    }

    public static string EnsureNotFileSystemRoot(
        string path,
        bool allowExistingRoot = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pathRoot = Path.GetPathRoot(fullPath)?.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(fullPath) ||
            string.Equals(fullPath, pathRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"拒绝使用文件系统根目录执行更新：{path}");
        }

        if (!allowExistingRoot && Directory.Exists(fullPath))
        {
            throw new IOException($"目标目录已经存在：{fullPath}");
        }

        return fullPath;
    }

    public static void RejectReparsePoint(string path)
    {
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException($"自动更新不支持符号链接或联接目录：{path}");
        }
    }

    public static void EnsureSiblingDirectory(
        string installationDirectory,
        string candidateDirectory,
        string expectedNamePrefix)
    {
        var installation = EnsureNotFileSystemRoot(installationDirectory, allowExistingRoot: true);
        var candidate = EnsureNotFileSystemRoot(candidateDirectory, allowExistingRoot: true);
        var installationParent = Path.GetDirectoryName(installation)
                                 ?? throw new InvalidOperationException("无法确定安装目录的父目录");
        var candidateParent = Path.GetDirectoryName(candidate)
                              ?? throw new InvalidOperationException("无法确定更新目录的父目录");
        if (!string.Equals(
                installationParent,
                candidateParent,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("候选目录和备份目录必须与安装目录位于同一父目录");
        }

        var name = Path.GetFileName(candidate);
        if (!name.StartsWith(expectedNamePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"更新目录名称不符合安全约束：{name}");
        }
    }

    public static string EnsureTrailingSeparator(string path)
    {
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
               Path.DirectorySeparatorChar;
    }

    private static void ValidatePathSegment(string segment, string originalPath)
    {
        if (segment is "." or ".." ||
            segment.EndsWith(' ') ||
            segment.EndsWith('.') ||
            segment.IndexOfAny(ExplicitInvalidFileNameChars) >= 0 ||
            segment.Any(static character => character < 32))
        {
            throw new InvalidDataException($"更新包包含非法 Windows 路径：{originalPath}");
        }

        var baseName = segment.Split('.')[0];
        if (WindowsReservedNames.Contains(baseName))
        {
            throw new InvalidDataException($"更新包包含 Windows 保留名称：{originalPath}");
        }
    }

    [GeneratedRegex("^[A-Za-z]:")]
    private static partial Regex DrivePrefixRegex();
}
