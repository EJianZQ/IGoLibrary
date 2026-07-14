using System.Text.Json;
using System.Text.Json.Serialization;

namespace IGoLibrary.Ex.Updater.Core;

public static class UpdateProtocol
{
    public const int SchemaVersion = 2;
    public const string ProductName = "IGoLibrary-Ex";
    public const string WindowsX64Runtime = "win-x64";
    public const string EntryExecutableName = "IGoLibrary.Ex.Desktop.exe";
    public const string UpdaterExecutableName = "IGoLibrary.Ex.Updater.exe";
    public const string ManifestFileName = "update-manifest.json";
    public const string PortableMarkerFileName = "portable-release.marker";
    public const string PortableMarkerContent = "IGoLibrary-Ex|portable|win-x64|2";

    public static JsonSerializerOptions JsonOptions { get; } = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}

public sealed record UpdatePackageManifest(
    int SchemaVersion,
    string Product,
    string Version,
    string Runtime,
    string EntryExecutable,
    IReadOnlyList<UpdateManifestFile> Files);

public sealed record UpdateManifestFile(
    string Path,
    long Size,
    string Sha256);

public sealed record UpdateTransactionRequest(
    int SchemaVersion,
    string TransactionId,
    int ParentProcessId,
    DateTimeOffset ParentProcessStartedAtUtc,
    string CurrentVersion,
    string TargetVersion,
    string InstallationDirectory,
    string StagingDirectory,
    string WorkingDirectory,
    string PackagePath,
    string CandidateDirectory,
    string BackupDirectory,
    string EntryExecutable,
    string ManifestFileName,
    string PackageDigest,
    long PackageSize,
    string HealthReportPath,
    string CoordinatorReadyPath,
    string WorkerReadyPath,
    string WorkerStatusPath,
    string DecisionPath,
    string HeartbeatPath,
    string LaunchedProcessPath,
    string LogDirectory);

public sealed record UpdateBootstrapPayload(
    int SchemaVersion,
    string SourceRequestPath,
    UpdateTransactionRequest Request);

public sealed record UpdateBootstrapResult(
    int SchemaVersion,
    string TransactionId,
    bool Succeeded,
    string Message,
    int? WorkerProcessId = null);

public enum UpdateCoordinatorSignalKind
{
    Ready,
    Canceled,
    Failed
}

public sealed record UpdateCoordinatorSignal(
    int SchemaVersion,
    string TransactionId,
    UpdateCoordinatorSignalKind Signal,
    string Message,
    DateTimeOffset CreatedAtUtc);

public enum UpdateWorkerPhase
{
    Starting,
    Ready,
    WaitingForParent,
    Preparing,
    Applying,
    Applied,
    Committing,
    Committed,
    RollingBack,
    RolledBack,
    Failed
}

public sealed record UpdateWorkerStatus(
    int SchemaVersion,
    string TransactionId,
    UpdateWorkerPhase Phase,
    string Message,
    DateTimeOffset UpdatedAtUtc,
    int? NewProcessId = null);

public enum UpdateDecisionKind
{
    Commit,
    Rollback
}

public sealed record UpdateDecision(
    int SchemaVersion,
    string TransactionId,
    UpdateDecisionKind Decision,
    DateTimeOffset CreatedAtUtc,
    int? NewProcessId = null);

public sealed record UpdateHealthReport(
    int SchemaVersion,
    string TransactionId,
    string Version,
    int ProcessId,
    DateTimeOffset CreatedAtUtc);

public sealed record UpdateLaunchedProcessInfo(
    int SchemaVersion,
    string TransactionId,
    int ProcessId,
    string ExecutablePath,
    DateTimeOffset CreatedAtUtc);

public sealed record UpdateCleanupAuthorization(
    int SchemaVersion,
    string TransactionId,
    DateTimeOffset CreatedAtUtc);

public sealed record UpdateCleanupCompletion(
    int SchemaVersion,
    string TransactionId,
    DateTimeOffset CreatedAtUtc);
