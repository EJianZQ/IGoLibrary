using System.Text.Json;
using System.Text.Json.Serialization;

namespace IGoLibrary.Ex.Infrastructure.Persistence;

internal static class AppJson
{
    public static JsonSerializerOptions Default { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };
}
