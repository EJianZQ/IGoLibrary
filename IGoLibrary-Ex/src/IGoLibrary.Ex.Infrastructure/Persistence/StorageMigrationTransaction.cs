using System.Security.Cryptography;
using IGoLibrary.Ex.Infrastructure.Logging;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

internal sealed record PendingStorageLocationChange(
    StorageLocations Source,
    StorageLocations Target,
    bool MigrateData,
    bool MigrateLogs,
    bool OverwriteTargetDatabase,
    DateTimeOffset RequestedAtUtc);

internal sealed class StorageMigrationTransaction(PendingStorageLocationChange change) : IDisposable
{
    private static readonly string[] DatabaseArtifactSuffixes = [string.Empty, "-wal", "-shm", "-journal"];
    private readonly string _id = Guid.NewGuid().ToString("N");
    private readonly List<(string Staged, string Target)> _stagedFiles = [];
    private readonly List<(string Backup, string Target)> _targetBackups = [];
    private readonly List<string> _targetsToBackup = [];
    private readonly List<string> _committedTargets = [];
    private readonly List<PendingStorageCleanup> _sourceFiles = [];
    private string? _dataStageDirectory;
    private string? _logStageDirectory;
    private bool _commitAccepted;

    public void Execute()
    {
        StageDatabase();
        StageLogs();
        CommitStagedFiles();
    }

    public void AcceptCommit()
    {
        _commitAccepted = true;
        foreach (var (backup, _) in _targetBackups)
        {
            TryDelete(backup);
        }
    }

    public List<PendingStorageCleanup> CleanupSourceFiles()
        => _commitAccepted ? TryDeleteFiles(_sourceFiles) : [];

    public void Rollback()
    {
        if (_commitAccepted)
        {
            return;
        }

        foreach (var target in _committedTargets.AsEnumerable().Reverse())
        {
            TryDelete(target);
        }

        foreach (var (backup, target) in _targetBackups.AsEnumerable().Reverse())
        {
            if (File.Exists(backup))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Move(backup, target, overwrite: true);
            }
        }
    }

    public void Dispose()
    {
        TryDeleteDirectory(_dataStageDirectory);
        TryDeleteDirectory(_logStageDirectory);
    }

    private void StageDatabase()
    {
        if (!change.MigrateData || StoragePathRules.DirectoriesReferToSameLocation(
                change.Source.DataDirectory,
                change.Target.DataDirectory))
        {
            return;
        }

        var sourceDatabase = Path.Combine(change.Source.DataDirectory, StorageLocationDefaults.DatabaseFileName);
        if (!File.Exists(sourceDatabase))
        {
            return;
        }

        var targetDatabase = Path.Combine(change.Target.DataDirectory, StorageLocationDefaults.DatabaseFileName);
        if (File.Exists(targetDatabase) && !change.OverwriteTargetDatabase)
        {
            throw new IOException("目标目录已经存在数据库，未获得覆盖确认");
        }

        _dataStageDirectory = Path.Combine(change.Target.DataDirectory, $".igolibrary-ex-data-migration-{_id}");
        Directory.CreateDirectory(_dataStageDirectory);
        foreach (var suffix in DatabaseArtifactSuffixes)
        {
            var source = sourceDatabase + suffix;
            if (!File.Exists(source))
            {
                continue;
            }

            var staged = Path.Combine(_dataStageDirectory, StorageLocationDefaults.DatabaseFileName + suffix);
            var target = targetDatabase + suffix;
            File.Copy(source, staged, overwrite: false);
            VerifyCopy(source, staged);
            _stagedFiles.Add((staged, target));
            _sourceFiles.Add(new PendingStorageCleanup(
                change.Source.DataDirectory,
                Path.GetFileName(source),
                StorageCleanupKind.DatabaseArtifact));
        }

        StorageDatabaseValidator.Validate(_stagedFiles.First(pair => pair.Target == targetDatabase).Staged);
        foreach (var suffix in DatabaseArtifactSuffixes)
        {
            var targetArtifact = targetDatabase + suffix;
            if (File.Exists(targetArtifact))
            {
                _targetsToBackup.Add(targetArtifact);
            }
        }
    }

    private void StageLogs()
    {
        if (StoragePathRules.DirectoriesReferToSameLocation(
                change.Source.LogDirectory,
                change.Target.LogDirectory))
        {
            return;
        }

        if (Directory.Exists(change.Target.LogDirectory))
        {
            foreach (var targetLegacyLog in Directory.GetFiles(
                         change.Target.LogDirectory,
                         "app-*.log",
                         SearchOption.TopDirectoryOnly)
                     .Where(path => AppLogFileCatalog.IsLegacyDailyFileName(Path.GetFileName(path))))
            {
                _sourceFiles.Add(new PendingStorageCleanup(
                    change.Target.LogDirectory,
                    Path.GetFileName(targetLegacyLog),
                    StorageCleanupKind.LogFile));
            }
        }

        if (!Directory.Exists(change.Source.LogDirectory))
        {
            return;
        }

        var sourceLogs = Directory.GetFiles(change.Source.LogDirectory, "app-*.log", SearchOption.TopDirectoryOnly);
        foreach (var legacyLog in sourceLogs.Where(path =>
                     AppLogFileCatalog.IsLegacyDailyFileName(Path.GetFileName(path))))
        {
            _sourceFiles.Add(new PendingStorageCleanup(
                change.Source.LogDirectory,
                Path.GetFileName(legacyLog),
                StorageCleanupKind.LogFile));
        }

        if (!change.MigrateLogs)
        {
            return;
        }

        var runLogs = sourceLogs
            .Where(path => AppLogFileCatalog.TryParseRunFileName(Path.GetFileName(path), out _))
            .ToArray();
        if (runLogs.Length == 0)
        {
            return;
        }

        _logStageDirectory = Path.Combine(change.Target.LogDirectory, $".igolibrary-ex-log-migration-{_id}");
        Directory.CreateDirectory(_logStageDirectory);
        foreach (var source in runLogs)
        {
            var staged = Path.Combine(_logStageDirectory, Path.GetFileName(source));
            File.Copy(source, staged, overwrite: false);
            VerifyCopy(source, staged);
            var target = GetUniqueLogTarget(change.Target.LogDirectory, Path.GetFileName(source));
            _stagedFiles.Add((staged, target));
            _sourceFiles.Add(new PendingStorageCleanup(
                change.Source.LogDirectory,
                Path.GetFileName(source),
                StorageCleanupKind.LogFile));
        }
    }

    private void CommitStagedFiles()
    {
        foreach (var target in _targetsToBackup)
        {
            var backupDirectory = _dataStageDirectory
                                  ?? throw new InvalidOperationException("数据库迁移暂存目录尚未创建");
            var backup = Path.Combine(backupDirectory, Path.GetFileName(target) + ".target-backup");
            File.Move(target, backup, overwrite: false);
            _targetBackups.Add((backup, target));
        }

        foreach (var (staged, target) in _stagedFiles)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Move(staged, target, overwrite: false);
            _committedTargets.Add(target);
        }
    }

    private static void VerifyCopy(string source, string destination)
    {
        var sourceInfo = new FileInfo(source);
        var destinationInfo = new FileInfo(destination);
        if (sourceInfo.Length != destinationInfo.Length || !HashesEqual(source, destination))
        {
            throw new IOException($"文件复制校验失败：{sourceInfo.Name}");
        }
    }

    private static bool HashesEqual(string left, string right)
    {
        using var leftStream = File.OpenRead(left);
        using var rightStream = File.OpenRead(right);
        return SHA256.HashData(leftStream).AsSpan().SequenceEqual(SHA256.HashData(rightStream));
    }

    private static string GetUniqueLogTarget(string targetDirectory, string fileName)
    {
        var candidate = Path.Combine(targetDirectory, fileName);
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        if (!AppLogFileCatalog.TryParseRunFileName(fileName, out var startedAt))
        {
            throw new InvalidDataException($"无法识别运行日志文件名：{fileName}");
        }

        return AppLogFileCatalog.GetAvailableRunPath(targetDirectory, startedAt);
    }

    private static List<PendingStorageCleanup> TryDeleteFiles(IEnumerable<PendingStorageCleanup> cleanups)
    {
        var failures = new List<PendingStorageCleanup>();
        var comparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var seenPaths = new HashSet<string>(comparer);
        foreach (var cleanup in cleanups)
        {
            if (!cleanup.TryGetFullPath(out var path) || !seenPaths.Add(path))
            {
                continue;
            }

            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                failures.Add(cleanup);
            }
        }

        return failures;
    }

    private static void TryDelete(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void TryDeleteDirectory(string? path)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
        }
    }
}
