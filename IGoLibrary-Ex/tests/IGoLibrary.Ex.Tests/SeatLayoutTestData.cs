using System.Text.Json.Nodes;
using IGoLibrary.Ex.Domain.Models;
using IGoLibrary.Ex.Infrastructure.Api;

namespace IGoLibrary.Ex.Tests;

internal static class SeatLayoutTestData
{
    public static string SampleJson()
    {
        using var stream = typeof(SeatLayoutTestData).Assembly.GetManifestResourceStream("IGoLibrary.Ex.Tests.Fixtures.library-layout-117580.json")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
    public static LibraryLayout Sample() => TraceIntGraphQlResponseMapper.MapLibraryLayout(SampleJson());
    public static LibraryLayout ParseItems(string items, Action<JsonObject>? configure = null)
    {
        var json = JsonNode.Parse(SampleJson())!;
        var layout = json["data"]!["userAuth"]!["reserve"]!["libs"]![0]!["lib_layout"]!.AsObject();
        layout["seats"] = JsonNode.Parse(items);
        configure?.Invoke(layout);
        return TraceIntGraphQlResponseMapper.MapLibraryLayout(json.ToJsonString());
    }
    public static LibraryLayout Small(int id = 1) => new(id, "测试场馆", "二层", true, 3, 1, 1,
    [
        new("a", "1", false, 0, 0) { SeatStatus = 1 },
        new("b", "2", true, 2, 0) { SeatStatus = 3 },
        new("c", "21", true, 2, 40) { SeatStatus = 2 }
    ]) { LayoutItems = [new(2, 0, 1, "desk", "", 0, false), new(8, 3, 1, "pillar", "柱", 0, false)] };
}
