using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Tests;

public sealed class UpdateProtocolJsonCompatibilityTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void SourceGeneratedMetadataPreservesEveryProtocolRootShape()
    {
        var timestamp = new DateTimeOffset(2026, 7, 14, 1, 2, 3, TimeSpan.Zero);
        var request = CreateRequest(timestamp);
        var manifest = new UpdatePackageManifest(
            UpdateProtocol.SchemaVersion,
            UpdateProtocol.ProductName,
            "1.2.3",
            UpdateProtocol.WindowsX64Runtime,
            UpdateProtocol.EntryExecutableName,
            [new UpdateManifestFile("目录/文件.dll", 123, new string('a', 64))]);

        AssertCompatible(
            manifest,
            UpdateJsonTypeInfo.PackageManifest,
            "package-manifest.json");
        AssertCompatible(
            request,
            UpdateJsonTypeInfo.TransactionRequest,
            "transaction-request.json");
        AssertCompatible(
            new UpdateBootstrapPayload(
                UpdateProtocol.SchemaVersion,
                request.WorkingDirectory + @"\request.json",
                request),
            UpdateJsonTypeInfo.BootstrapPayload,
            "bootstrap-payload.json");
        AssertCompatible(
            new UpdateBootstrapResult(
                UpdateProtocol.SchemaVersion,
                "transaction-1",
                true,
                "ready",
                null),
            UpdateJsonTypeInfo.BootstrapResult,
            "bootstrap-result.json");
        AssertCompatible(
            new UpdateCoordinatorSignal(
                UpdateProtocol.SchemaVersion,
                "transaction-1",
                UpdateCoordinatorSignalKind.Ready,
                "ready",
                timestamp),
            UpdateJsonTypeInfo.CoordinatorSignal,
            "coordinator-signal.json");
        AssertCompatible(
            new UpdateWorkerStatus(
                UpdateProtocol.SchemaVersion,
                "transaction-1",
                UpdateWorkerPhase.WaitingForParent,
                "waiting",
                timestamp),
            UpdateJsonTypeInfo.WorkerStatus,
            "worker-status.json");
        AssertCompatible(
            new UpdateDecision(
                UpdateProtocol.SchemaVersion,
                "transaction-1",
                UpdateDecisionKind.Rollback,
                timestamp),
            UpdateJsonTypeInfo.Decision,
            "decision.json");
        AssertCompatible(
            new UpdateHealthReport(
                UpdateProtocol.SchemaVersion,
                "transaction-1",
                "1.2.3",
                42,
                timestamp),
            UpdateJsonTypeInfo.HealthReport,
            "health-report.json");
        AssertCompatible(
            new UpdateLaunchedProcessInfo(
                UpdateProtocol.SchemaVersion,
                "transaction-1",
                42,
                @"C:\我去图书馆\IGoLibrary.Ex.Desktop.exe",
                timestamp),
            UpdateJsonTypeInfo.LaunchedProcessInfo,
            "launched-process.json");
        AssertCompatible(
            new UpdateCleanupAuthorization(
                UpdateProtocol.SchemaVersion,
                "transaction-1",
                timestamp),
            UpdateJsonTypeInfo.CleanupAuthorization,
            "cleanup-authorization.json");
        AssertCompatible(
            new UpdateCleanupCompletion(
                UpdateProtocol.SchemaVersion,
                "transaction-1",
                timestamp),
            UpdateJsonTypeInfo.CleanupCompletion,
            "cleanup-completion.json");
    }

    [Fact]
    public void SourceGeneratedEnumsRemainCamelCase()
    {
        var timestamp = new DateTimeOffset(2026, 7, 14, 1, 2, 3, TimeSpan.Zero);

        var signalJson = JsonSerializer.Serialize(
            new UpdateCoordinatorSignal(2, "tx", UpdateCoordinatorSignalKind.Ready, "ok", timestamp),
            UpdateJsonTypeInfo.CoordinatorSignal);
        var statusJson = JsonSerializer.Serialize(
            new UpdateWorkerStatus(2, "tx", UpdateWorkerPhase.WaitingForParent, "wait", timestamp),
            UpdateJsonTypeInfo.WorkerStatus);
        var decisionJson = JsonSerializer.Serialize(
            new UpdateDecision(2, "tx", UpdateDecisionKind.Rollback, timestamp),
            UpdateJsonTypeInfo.Decision);

        Assert.Contains("\"signal\": \"ready\"", signalJson, StringComparison.Ordinal);
        Assert.Contains("\"phase\": \"waitingForParent\"", statusJson, StringComparison.Ordinal);
        Assert.Contains("\"decision\": \"rollback\"", decisionJson, StringComparison.Ordinal);
    }

    [Fact]
    public void AtomicFileRoundTripUsesUtf8WithoutBomAndCaseInsensitiveReading()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "请求 中文.json");
        var expected = CreateRequest(new DateTimeOffset(2026, 7, 14, 1, 2, 3, TimeSpan.Zero));

        UpdateJsonFile.WriteAtomic(path, expected, UpdateJsonTypeInfo.TransactionRequest);
        var bytes = File.ReadAllBytes(path);
        var actual = UpdateJsonFile.Read(path, UpdateJsonTypeInfo.TransactionRequest);

        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Equal(expected, actual);

        var bootstrapResult = JsonSerializer.Deserialize(
            """
            {"SCHEMAVERSION":2,"TRANSACTIONID":"tx","SUCCEEDED":true,"MESSAGE":"ok","WORKERPROCESSID":null}
            """,
            UpdateJsonTypeInfo.BootstrapResult);
        Assert.Equal("tx", bootstrapResult!.TransactionId);
        Assert.Null(bootstrapResult.WorkerProcessId);
    }

    [Fact]
    public async Task AsyncAtomicFileRoundTripUsesSourceGeneratedMetadata()
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "异步 请求.json");
        var expected = new UpdateBootstrapResult(
            UpdateProtocol.SchemaVersion,
            "transaction-async",
            false,
            "等待恢复",
            null);

        await UpdateJsonFile.WriteAtomicAsync(
            path,
            expected,
            UpdateJsonTypeInfo.BootstrapResult);
        var actual = await UpdateJsonFile.ReadAsync(
            path,
            UpdateJsonTypeInfo.BootstrapResult);
        var bytes = await File.ReadAllBytesAsync(path);

        Assert.Equal(expected, actual);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp-*"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static void AssertCompatible<T>(
        T value,
        JsonTypeInfo<T> typeInfo,
        string fixtureName)
    {
        var expected = JsonSerializer.Serialize(value, CreateLegacyOptions());
        var actual = JsonSerializer.Serialize(value, typeInfo);
        Assert.Equal(expected, actual);

        var fixturePath = Path.Combine(
            FindProjectRoot(),
            "tests",
            "IGoLibrary.Ex.Tests",
            "Fixtures",
            "UpdateProtocol",
            fixtureName);
        var golden = File.ReadAllText(fixturePath);
        Assert.Equal(NormalizeLineEndings(golden), NormalizeLineEndings(actual));
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.ReplaceLineEndings("\n").TrimEnd();
    }

    private static string FindProjectRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "IGoLibrary-Ex.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate IGoLibrary-Ex.sln.");
    }

    private static JsonSerializerOptions CreateLegacyOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(
            new JsonStringEnumConverter<UpdateCoordinatorSignalKind>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(
            new JsonStringEnumConverter<UpdateWorkerPhase>(JsonNamingPolicy.CamelCase));
        options.Converters.Add(
            new JsonStringEnumConverter<UpdateDecisionKind>(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static UpdateTransactionRequest CreateRequest(DateTimeOffset timestamp)
    {
        const string root = @"C:\我去图书馆\更新 事务";
        return new UpdateTransactionRequest(
            UpdateProtocol.SchemaVersion,
            "transaction-1",
            42,
            timestamp,
            "1.2.2",
            "1.2.3",
            @"C:\我去图书馆",
            root + @"\staging",
            root,
            root + @"\package.zip",
            root + @"\candidate",
            root + @"\backup",
            UpdateProtocol.EntryExecutableName,
            UpdateProtocol.ManifestFileName,
            "sha256:" + new string('a', 64),
            123,
            root + @"\health.json",
            root + @"\coordinator-ready.json",
            root + @"\worker-ready.json",
            root + @"\worker-status.json",
            root + @"\decision.json",
            root + @"\heartbeat.txt",
            root + @"\launched-process.json",
            root + @"\logs");
    }
}
