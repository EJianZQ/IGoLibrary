using System.Globalization;

namespace IGoLibrary.Ex.Infrastructure.Logging;

internal static class AppLogFileCatalog
{
    private const string Prefix = "app-";
    private const string Extension = ".log";
    private const string RunTimestampFormat = "yyyyMMdd-HHmmss-fff";
    private const string LegacyDateFormat = "yyyyMMdd";

    public static string BuildRunFileName(DateTimeOffset startedAt, int collisionIndex = 0)
    {
        var suffix = collisionIndex <= 0 ? string.Empty : $"-{collisionIndex:00}";
        return $"{Prefix}{startedAt.ToString(RunTimestampFormat, CultureInfo.InvariantCulture)}{suffix}{Extension}";
    }

    public static (string Path, FileStream Stream) CreateRunFile(
        string directory,
        DateTimeOffset startedAt)
    {
        Directory.CreateDirectory(directory);
        for (var collisionIndex = 0; ; collisionIndex++)
        {
            var path = Path.Combine(directory, BuildRunFileName(startedAt, collisionIndex));
            try
            {
                var stream = new FileStream(
                    path,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    bufferSize: 4096,
                    useAsync: true);
                return (path, stream);
            }
            catch (IOException) when (File.Exists(path))
            {
            }
        }
    }

    public static string GetAvailableRunPath(
        string directory,
        DateTimeOffset startedAt)
    {
        for (var collisionIndex = 0; ; collisionIndex++)
        {
            var path = Path.Combine(directory, BuildRunFileName(startedAt, collisionIndex));
            if (!File.Exists(path))
            {
                return path;
            }
        }
    }

    public static bool TryParseRunFileName(string fileName, out DateTimeOffset startedAt)
    {
        startedAt = default;
        var stem = GetManagedStem(fileName);
        if (stem is null || stem.Length < RunTimestampFormat.Length)
        {
            return false;
        }

        var timestampText = stem[..RunTimestampFormat.Length];
        var suffix = stem[RunTimestampFormat.Length..];
        if (suffix.Length > 0 &&
            (suffix[0] != '-' || suffix.Length == 1 || !suffix[1..].All(char.IsDigit)))
        {
            return false;
        }

        if (!DateTime.TryParseExact(
                timestampText,
                RunTimestampFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var localDateTime))
        {
            return false;
        }

        startedAt = new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime));
        return true;
    }

    public static bool IsLegacyDailyFileName(string fileName)
    {
        var stem = GetManagedStem(fileName);
        return stem is not null &&
               DateOnly.TryParseExact(
                   stem,
                   LegacyDateFormat,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.None,
                   out _);
    }

    public static int DeleteLegacyDailyFiles(string directory)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            var failureCount = 0;
            foreach (var path in Directory.EnumerateFiles(directory, $"{Prefix}*{Extension}", SearchOption.TopDirectoryOnly))
            {
                if (!IsLegacyDailyFileName(Path.GetFileName(path)))
                {
                    continue;
                }

                try
                {
                    File.Delete(path);
                }
                catch
                {
                    failureCount++;
                }
            }

            return failureCount;
        }
        catch
        {
            return 1;
        }
    }

    public static int EnforceRetention(
        string directory,
        int retainedFileCount,
        string? protectedPath = null)
    {
        try
        {
            if (!Directory.Exists(directory))
            {
                return 0;
            }

            var pathComparer = OperatingSystem.IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            var files = new DirectoryInfo(directory)
                .EnumerateFiles($"{Prefix}*{Extension}", SearchOption.TopDirectoryOnly)
                .Select(file => new
                {
                    File = file,
                    Parsed = TryParseRunFileName(file.Name, out var startedAt),
                    StartedAt = startedAt
                })
                .Where(item => item.Parsed)
                .OrderByDescending(item => item.StartedAt)
                .ThenByDescending(item => item.File.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var keep = new HashSet<string>(pathComparer);
            if (!string.IsNullOrWhiteSpace(protectedPath) && File.Exists(protectedPath))
            {
                keep.Add(Path.GetFullPath(protectedPath));
            }

            foreach (var item in files)
            {
                if (keep.Count >= retainedFileCount)
                {
                    break;
                }

                keep.Add(item.File.FullName);
            }

            var failureCount = 0;
            foreach (var item in files)
            {
                if (keep.Contains(item.File.FullName))
                {
                    continue;
                }

                try
                {
                    item.File.Delete();
                }
                catch
                {
                    failureCount++;
                }
            }

            return failureCount;
        }
        catch
        {
            return 1;
        }
    }

    private static string? GetManagedStem(string fileName)
    {
        if (!fileName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
            !fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return fileName[Prefix.Length..^Extension.Length];
    }
}
