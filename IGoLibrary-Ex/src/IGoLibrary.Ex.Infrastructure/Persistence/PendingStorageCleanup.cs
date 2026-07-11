using IGoLibrary.Ex.Infrastructure.Logging;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

internal enum StorageCleanupKind
{
    DatabaseArtifact,
    LogFile
}

internal sealed record PendingStorageCleanup(
    string SourceDirectory,
    string FileName,
    StorageCleanupKind Kind)
{
    public bool TryGetFullPath(out string fullPath)
    {
        fullPath = string.Empty;
        if (string.IsNullOrWhiteSpace(FileName) ||
            !string.Equals(FileName, Path.GetFileName(FileName), StringComparison.Ordinal) ||
            !IsExpectedFileName(FileName, Kind))
        {
            return false;
        }

        try
        {
            var directory = StoragePathRules.NormalizeDirectory(SourceDirectory, nameof(SourceDirectory));
            fullPath = Path.Combine(directory, FileName);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    public static bool TryCreateFromLegacyPath(string path, out PendingStorageCleanup? cleanup)
    {
        cleanup = null;
        try
        {
            var fullPath = Path.GetFullPath(path);
            var directory = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);
            if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
            {
                return false;
            }

            var kind = StorageLocationDefaults.IsDatabaseArtifactFileName(fileName)
                ? StorageCleanupKind.DatabaseArtifact
                : StorageCleanupKind.LogFile;
            if (!IsExpectedFileName(fileName, kind))
            {
                return false;
            }

            cleanup = new PendingStorageCleanup(
                StoragePathRules.NormalizeDirectory(directory, nameof(path)),
                fileName,
                kind);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsExpectedFileName(string fileName, StorageCleanupKind kind)
    {
        return kind switch
        {
            StorageCleanupKind.DatabaseArtifact => StorageLocationDefaults.IsDatabaseArtifactFileName(fileName),
            StorageCleanupKind.LogFile =>
                AppLogFileCatalog.IsLegacyDailyFileName(fileName) ||
                AppLogFileCatalog.TryParseRunFileName(fileName, out _),
            _ => false
        };
    }
}
