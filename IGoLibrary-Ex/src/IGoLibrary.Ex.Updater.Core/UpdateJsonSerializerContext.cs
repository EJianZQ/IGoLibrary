using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace IGoLibrary.Ex.Updater.Core;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = true,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(UpdatePackageManifest))]
[JsonSerializable(typeof(UpdateManifestFile))]
[JsonSerializable(typeof(UpdateTransactionRequest))]
[JsonSerializable(typeof(UpdateBootstrapPayload))]
[JsonSerializable(typeof(UpdateBootstrapResult))]
[JsonSerializable(typeof(UpdateCoordinatorSignal))]
[JsonSerializable(typeof(UpdateWorkerStatus))]
[JsonSerializable(typeof(UpdateDecision))]
[JsonSerializable(typeof(UpdateHealthReport))]
[JsonSerializable(typeof(UpdateLaunchedProcessInfo))]
[JsonSerializable(typeof(UpdateCleanupAuthorization))]
[JsonSerializable(typeof(UpdateCleanupCompletion))]
internal sealed partial class UpdateJsonSerializerContext : JsonSerializerContext
{
    internal static UpdateJsonSerializerContext Protocol { get; } = new(CreateOptions());

    private static JsonSerializerOptions CreateOptions()
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
}

public static class UpdateJsonTypeInfo
{
    public static JsonTypeInfo<UpdatePackageManifest> PackageManifest =>
        UpdateJsonSerializerContext.Protocol.UpdatePackageManifest;

    public static JsonTypeInfo<UpdateTransactionRequest> TransactionRequest =>
        UpdateJsonSerializerContext.Protocol.UpdateTransactionRequest;

    public static JsonTypeInfo<UpdateBootstrapPayload> BootstrapPayload =>
        UpdateJsonSerializerContext.Protocol.UpdateBootstrapPayload;

    public static JsonTypeInfo<UpdateBootstrapResult> BootstrapResult =>
        UpdateJsonSerializerContext.Protocol.UpdateBootstrapResult;

    public static JsonTypeInfo<UpdateCoordinatorSignal> CoordinatorSignal =>
        UpdateJsonSerializerContext.Protocol.UpdateCoordinatorSignal;

    public static JsonTypeInfo<UpdateWorkerStatus> WorkerStatus =>
        UpdateJsonSerializerContext.Protocol.UpdateWorkerStatus;

    public static JsonTypeInfo<UpdateDecision> Decision =>
        UpdateJsonSerializerContext.Protocol.UpdateDecision;

    public static JsonTypeInfo<UpdateHealthReport> HealthReport =>
        UpdateJsonSerializerContext.Protocol.UpdateHealthReport;

    public static JsonTypeInfo<UpdateLaunchedProcessInfo> LaunchedProcessInfo =>
        UpdateJsonSerializerContext.Protocol.UpdateLaunchedProcessInfo;

    public static JsonTypeInfo<UpdateCleanupAuthorization> CleanupAuthorization =>
        UpdateJsonSerializerContext.Protocol.UpdateCleanupAuthorization;

    public static JsonTypeInfo<UpdateCleanupCompletion> CleanupCompletion =>
        UpdateJsonSerializerContext.Protocol.UpdateCleanupCompletion;
}
