using System.Text.Json;
using System.Text.Json.Serialization;

namespace IGoLibrary.Ex.Desktop.Services;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = true,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(VerifiedUpdateCache))]
internal sealed partial class DesktopUpdateJsonSerializerContext : JsonSerializerContext;
