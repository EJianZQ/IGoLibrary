using System.Text.Json;
using IGoLibrary.Ex.Updater;

namespace IGoLibrary.Ex.Updater.Tests;

public sealed class UpdaterLogSanitizerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreservesNestedJsonFieldsAfterUrl(bool relaxedEscaping)
    {
        var options = new JsonSerializerOptions
        {
            Encoder = relaxedEscaping ? System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping : System.Text.Encodings.Web.JavaScriptEncoder.Default
        };
        var nested = JsonSerializer.Serialize(new { url = "https://a.test/?x=private-value&y=private-value", asset = "release.zip" }, options);
        var result = JsonSerializer.Serialize(new { message = nested, status = 200 }, options);
        for (var i = 0; i < 4; i++)
        {
            result = UpdaterLogSanitizer.Sanitize(result);
            using var outer = JsonDocument.Parse(result);
            using var inner = JsonDocument.Parse(outer.RootElement.GetProperty("message").GetString()!);
            Assert.Equal("release.zip", inner.RootElement.GetProperty("asset").GetString());
            Assert.Equal(200, outer.RootElement.GetProperty("status").GetInt32());
            Assert.DoesNotContain("private-value", result);
        }
    }

    [Theory]
    [InlineData("public\\u0026y=private-value")]
    [InlineData("public\\u0022private-value")]
    [InlineData("public\\u4E2Dprivate-value")]
    [InlineData("public\\\\private-value")]
    [InlineData("public'private-value")]
    public void SerializedQueryEscapesAreFullyRedacted(string query)
    {
        var payload = "{\"url\":\"https://a.test/file?x=" + query + "\",\"asset\":\"release.zip\"}";
        var result = UpdaterLogSanitizer.Sanitize(payload);
        using var parsed = JsonDocument.Parse(result);
        Assert.DoesNotContain("private-value", parsed.RootElement.GetProperty("url").GetString());
        Assert.Equal("release.zip", parsed.RootElement.GetProperty("asset").GetString());
        Assert.Equal(result, UpdaterLogSanitizer.Sanitize(result));
    }

    [Theory]
    [InlineData("地址=https://a.test/?x=public'private-value；asset=release.zip")]
    [InlineData("<a href='https://a.test/?x=private-value'>release.zip</a>")]
    public void QueryQuoteBoundariesDoNotLeakOrRemoveFollowingFields(string payload)
    {
        var result = UpdaterLogSanitizer.Sanitize(payload);
        Assert.DoesNotContain("private-value", result);
        Assert.Contains("release.zip", result);
        Assert.Equal(result, UpdaterLogSanitizer.Sanitize(result));
    }

    [Theory]
    [InlineData("private-value")]
    [InlineData("<redacted>")]
    [InlineData("<redacted>private-value")]
    public void RepeatedQuerySanitizationPreservesAdjacentJsonFields(string query)
    {
        var result = JsonSerializer.Serialize(new { url = "https://a.test/file?x=" + query, asset = "release.zip", status = 200 });
        for (var i = 0; i < 4; i++)
        {
            result = UpdaterLogSanitizer.Sanitize(result);
            using var parsed = JsonDocument.Parse(result);
            Assert.Equal("release.zip", parsed.RootElement.GetProperty("asset").GetString());
            Assert.Equal(200, parsed.RootElement.GetProperty("status").GetInt32());
            Assert.DoesNotContain("private-value", parsed.RootElement.GetProperty("url").GetString());
        }
    }

    [Theory]
    [InlineData("<redacted>")]
    [InlineData(@"\u003Credacted\u003E")]
    [InlineData("&lt;redacted&gt;")]
    public void PreservesEncodedQueryPlaceholdersAndFollowingLogFields(string placeholder)
    {
        var original = $"地址=https://a.test/file?{placeholder}；状态码=200";
        var result = UpdaterLogSanitizer.Sanitize(original);
        Assert.Equal(original, result);
        Assert.Equal(result, UpdaterLogSanitizer.Sanitize(result));
    }
}
