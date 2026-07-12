namespace IGoLibrary.Ex.Infrastructure.Persistence;

internal static class StoragePathRules
{
    public static StorageLocations Normalize(StorageLocations locations)
    {
        ArgumentNullException.ThrowIfNull(locations);
        return new StorageLocations(
            NormalizeDirectory(locations.DataDirectory, nameof(locations.DataDirectory)),
            NormalizeDirectory(locations.LogDirectory, nameof(locations.LogDirectory)));
    }

    public static string NormalizeDirectory(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("存储目录不能为空", parameterName);
        }

        var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(value.Trim()));
        var root = Path.TrimEndingDirectorySeparator(Path.GetPathRoot(fullPath) ?? string.Empty);
        if (string.IsNullOrWhiteSpace(root) || PathsEqualWithoutNormalization(fullPath, root))
        {
            throw new ArgumentException("不能把文件系统根目录作为存储目录", parameterName);
        }

        return fullPath;
    }

    public static bool PathsEqual(string left, string right)
    {
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            comparison);
    }

    public static bool DirectoriesReferToSameLocation(string left, string right)
    {
        if (PathsEqual(left, right))
        {
            return true;
        }

        var resolvedLeft = TryResolvePhysicalDirectory(left);
        var resolvedRight = TryResolvePhysicalDirectory(right);
        if (resolvedLeft is not null &&
            resolvedRight is not null &&
            PathsEqual(resolvedLeft, resolvedRight))
        {
            return true;
        }

        return ProbeDirectoryIdentity(left, right);
    }

    public static void ValidateWritable(StorageLocations locations)
    {
        ProbeWritableDirectory(locations.DataDirectory);
        if (!DirectoriesReferToSameLocation(locations.DataDirectory, locations.LogDirectory))
        {
            ProbeWritableDirectory(locations.LogDirectory);
        }
    }

    public static void EnsureDirectories(StorageLocations locations)
    {
        Directory.CreateDirectory(locations.DataDirectory);
        Directory.CreateDirectory(locations.LogDirectory);
    }

    private static bool PathsEqualWithoutNormalization(string left, string right)
        => string.Equals(
            left,
            right,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string? TryResolvePhysicalDirectory(string directory)
    {
        try
        {
            var fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (!Directory.Exists(fullPath))
            {
                return null;
            }

            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            var current = root;
            var remainder = fullPath[root.Length..];
            var segments = remainder.Split(
                [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            foreach (var segment in segments)
            {
                var candidate = Path.Combine(current, segment);
                var resolved = new DirectoryInfo(candidate).ResolveLinkTarget(returnFinalTarget: true);
                current = resolved?.FullName ?? candidate;
            }

            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(current));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or
                                       NotSupportedException)
        {
            return null;
        }
    }

    private static bool ProbeDirectoryIdentity(string left, string right)
    {
        if (!Directory.Exists(left) || !Directory.Exists(right))
        {
            return false;
        }

        var fileName = $".igolibrary-ex-directory-identity-{Guid.NewGuid():N}.tmp";
        var leftProbe = Path.Combine(left, fileName);
        var rightProbe = Path.Combine(right, fileName);
        try
        {
            using var stream = new FileStream(
                leftProbe,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete,
                1,
                FileOptions.WriteThrough);
            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);
            return File.Exists(rightProbe);
        }
        catch
        {
            return false;
        }
        finally
        {
            try
            {
                if (File.Exists(leftProbe))
                {
                    File.Delete(leftProbe);
                }
            }
            catch
            {
            }
        }
    }

    private static void ProbeWritableDirectory(string directory)
    {
        Directory.CreateDirectory(directory);
        var probePath = Path.Combine(directory, $".igolibrary-ex-write-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                1,
                FileOptions.WriteThrough);
            stream.WriteByte(0);
            stream.Flush(flushToDisk: true);
        }
        finally
        {
            if (File.Exists(probePath))
            {
                File.Delete(probePath);
            }
        }
    }
}
