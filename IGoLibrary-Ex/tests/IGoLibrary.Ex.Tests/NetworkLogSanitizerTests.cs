using System.Text;
using IGoLibrary.Ex.Infrastructure.Logging;

namespace IGoLibrary.Ex.Tests;

public sealed class NetworkLogSanitizerTests
{
    [Theory]
    [InlineData("public\\u0026y=private-value")]
    [InlineData("public\\u0022private-value")]
    [InlineData("public\\u4E2Dprivate-value")]
    [InlineData("public\\\\private-value")]
    [InlineData("public'private-value")]
    public void SerializedUrlQueryEscapesDoNotLeaveSecretSuffixes(string query)
    {
        var payload = "{\"url\":\"https://a.test/file?x=" + query + "\",\"seat\":\"A001\"}";
        foreach (var sanitize in new Func<string, string>[] { NetworkLogSanitizer.Text, IGoLibrary.Ex.Application.Logging.AppLogSanitizer.Sanitize })
        {
            var result = sanitize(payload);
            using var parsed = System.Text.Json.JsonDocument.Parse(result);
            Assert.DoesNotContain("private-value", parsed.RootElement.GetProperty("url").GetString());
            Assert.Equal("A001", parsed.RootElement.GetProperty("seat").GetString());
            Assert.Equal(result, sanitize(result));
        }
    }

    [Theory]
    [InlineData("地址=https://a.test/?x=public'private-value；seat=A001")]
    [InlineData("<a href='https://a.test/?x=private-value'>A001</a>")]
    [InlineData("<a href=\"https://a.test/?x=public'private-value\">A001</a>")]
    public void UrlQuoteBoundariesRespectQuotedAndUnquotedValues(string payload)
    {
        foreach (var sanitize in new Func<string, string>[] { NetworkLogSanitizer.Text, IGoLibrary.Ex.Application.Logging.AppLogSanitizer.Sanitize })
        {
            var result = sanitize(payload);
            Assert.DoesNotContain("private-value", result);
            Assert.Contains("A001", result);
            if (payload.Contains("<a")) Assert.Contains(">A001</a>", result);
            Assert.Equal(result, sanitize(result));
        }
    }

    [Fact]
    public void RepeatedUrlSanitizationPreservesNestedJsonBusinessFields()
    {
        var nested = System.Text.Json.JsonSerializer.Serialize(new { url = "https://a.test/?x=private-value", seat = "A001" });
        var payload = System.Text.Json.JsonSerializer.Serialize(new { message = nested, status = 200 });
        var result = NetworkLogSanitizer.Body(Encoding.UTF8.GetBytes(payload), "application/json");
        for (var i = 0; i < 4; i++)
        {
            result = IGoLibrary.Ex.Application.Logging.AppLogSanitizer.Sanitize(NetworkLogSanitizer.Text(result));
            using var outer = System.Text.Json.JsonDocument.Parse(result);
            using var inner = System.Text.Json.JsonDocument.Parse(outer.RootElement.GetProperty("message").GetString()!);
            Assert.Equal("A001", inner.RootElement.GetProperty("seat").GetString());
            Assert.Equal(200, outer.RootElement.GetProperty("status").GetInt32());
            Assert.DoesNotContain("private-value", result);
        }
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void SerializedNestedUrlQuotesDelimitOnlyTheUrl(bool relaxedEscaping, int depth)
    {
        var options = new System.Text.Json.JsonSerializerOptions
        {
            Encoder = relaxedEscaping ? System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping : System.Text.Encodings.Web.JavaScriptEncoder.Default
        };
        var payload = System.Text.Json.JsonSerializer.Serialize(new { url = "https://a.test/?x=private-value&y=private-value", seat = "A001" }, options);
        for (var i = 0; i < depth; i++) payload = System.Text.Json.JsonSerializer.Serialize(new { message = payload, status = 200 }, options);
        foreach (var sanitize in new Func<string, string>[] { NetworkLogSanitizer.Text, IGoLibrary.Ex.Application.Logging.AppLogSanitizer.Sanitize })
        {
            var result = sanitize(payload);
            Assert.DoesNotContain("private-value", result);
            for (var i = 0; i < depth; i++)
            {
                using var outer = System.Text.Json.JsonDocument.Parse(result);
                Assert.Equal(200, outer.RootElement.GetProperty("status").GetInt32());
                result = outer.RootElement.GetProperty("message").GetString()!;
            }
            using var inner = System.Text.Json.JsonDocument.Parse(result);
            Assert.Equal("A001", inner.RootElement.GetProperty("seat").GetString());
        }
    }

    [Theory]
    [InlineData("application/x-www-form-urlencoded", "t=first-secret&T=second-secret&%74=third-secret&seat=A001")]
    [InlineData("application/x-www-form-urlencoded", "t=first-secret&t=second-secret&seat=A001")]
    [InlineData("application/x-www-form-urlencoded", "message=%7B%22t%22%3A%22first-secret%22%7D&seat=A001")]
    [InlineData("application/json", "{\"\\u0074\":\"first-secret\",\"seat\":\"A001\"}")]
    [InlineData("application/json", "{\"message\":\"t=first-secret\",\"seat\":\"A001\"}")]
    [InlineData("application/xml", "<root t='first-secret'><T>second-secret</T><seat>A001</seat></root>")]
    [InlineData("text/plain", "t=first-secret T:second-secret seat=A001")]
    public void RemoteSessionAliasIsHiddenInEverySupportedRepresentation(string type, string payload)
    {
        var result = NetworkLogSanitizer.Body(Encoding.UTF8.GetBytes(payload), type);
        Assert.DoesNotContain("first-secret", result);
        Assert.DoesNotContain("second-secret", result);
        Assert.DoesNotContain("third-secret", result);
        Assert.Contains("A001", result);
    }

    [Theory]
    [InlineData("https://a.test/path?x=private-value")]
    [InlineData("https://a.test/path?x=private-value#private-value")]
    [InlineData("https://api.telegram.org/bot123:private-value/sendMessage?x=private-value")]
    public void RepeatedUrlSanitizationPreservesJsonAndAdjacentBusinessFields(string url)
    {
        var payload = System.Text.Json.JsonSerializer.Serialize(new { url, seat = "A001", status = 200 });
        var result = NetworkLogSanitizer.Body(Encoding.UTF8.GetBytes(payload), "application/json");
        for (var i = 0; i < 4; i++)
        {
            result = IGoLibrary.Ex.Application.Logging.AppLogSanitizer.Sanitize(NetworkLogSanitizer.Text(result));
            using var parsed = System.Text.Json.JsonDocument.Parse(result);
            Assert.Equal("A001", parsed.RootElement.GetProperty("seat").GetString());
            Assert.Equal(200, parsed.RootElement.GetProperty("status").GetInt32());
            Assert.DoesNotContain("private-value", result);
            Assert.DoesNotContain("private-value", parsed.RootElement.GetProperty("url").GetString());
        }
    }

    [Fact]
    public void RepeatedUrlSanitizationPreservesXmlAndItsEncodedPlaceholder()
    {
        const string payload = "<root><url>https://a.test/path?x=private-value&amp;y=private-value</url><seat>A001</seat></root>";
        var result = NetworkLogSanitizer.Body(Encoding.UTF8.GetBytes(payload), "application/xml");
        for (var i = 0; i < 4; i++)
        {
            result = IGoLibrary.Ex.Application.Logging.AppLogSanitizer.Sanitize(NetworkLogSanitizer.Text(result));
            var parsed = System.Xml.Linq.XDocument.Parse(result);
            Assert.Equal("A001", parsed.Root!.Element("seat")!.Value);
            Assert.Equal("https://a.test/path?<redacted>", parsed.Root.Element("url")!.Value);
            Assert.DoesNotContain("private-value", result);
        }
    }

    [Theory]
    [InlineData("https://a.test/path")]
    [InlineData("https://a.test/path?x=private-value")]
    public void UrlDoesNotConsumeFollowingLogFields(string url)
    {
        var logs = new NetworkLogTestContext();
        logs.Logger.Start("test", "出站", "GET", url);
        Assert.Contains("地址=https://a.test/path", logs.Writer.Text);
        Assert.Contains("；任务=", logs.Writer.Text);
        Assert.DoesNotContain("%EF%BC%9B", logs.Writer.Text);
        Assert.DoesNotContain("private-value", logs.Writer.Text);
    }

    [Fact]
    public void RepeatedSanitizationPreservesAdjacentXmlAndStillRedactsBracketedSecrets()
    {
        var text = "<seat token=\"private-secret\">A001</seat> password=<another-secret> token=<redacted>marker-secret";
        for (var i = 0; i < 4; i++)
        {
            text = NetworkLogSanitizer.Text(text);
            text = IGoLibrary.Ex.Application.Logging.AppLogSanitizer.Sanitize(text);
            Assert.Contains("A001", text);
            Assert.DoesNotContain("private-secret", text);
            Assert.DoesNotContain("another-secret", text);
            Assert.DoesNotContain("marker-secret", text);
        }
    }
    [Theory]
    [InlineData("https://user:secret@example.com/a?code=secret#secret", null)]
    [InlineData("https://api.telegram.org/bot123:secret/sendMessage", null)]
    [InlineData("https://api.telegram.org/bot123%3Asecret/sendMessage", null)]
    [InlineData("https://sctapi.ftqq.com/secret.send", null)]
    [InlineData("https://123.push.ft07.com/send/secret.send", null)]
    [InlineData("https://api.day.app/secret", null)]
    [InlineData("https://custom.example.com/base/secret", "BarkAlertSender")]
    [InlineData("https://example.com/_igolibrary/health/secret", null)]
    public void Address_HidesCredentialLocations(string address, string? service)
    {
        var result = NetworkLogSanitizer.Address(address, service);
        Assert.DoesNotContain("secret", result);
        Assert.DoesNotContain("user:", result);
    }

    [Theory]
    [InlineData("application/json", "{\"access_token\":\"secret\",\"data\":[{\"appToken\":\"secret\",\"seat\":\"A001\"}]}")]
    [InlineData("application/json", "{\"credentials\":{\"value\":\"secret\"},\"seat\":\"A001\"}")]
    [InlineData("application/json", "{\"message\":\"{\\\"password\\\":\\\"secret\\\",\\\"seat\\\":\\\"A001\\\"}\"}")]
    [InlineData("application/json", "\"password=secret A001\"")]
    [InlineData("text/plain", "password=secret A001")]
    [InlineData("text/plain", "{\"token\":\"secret\",\"seat\":\"A001\"}")]
    [InlineData("application/x-www-form-urlencoded", "app%54oken=secret&seat=A001")]
    [InlineData("application/xml", "<root><Password>secret</Password><seat token='secret'>A001</seat></root>")]
    public void Body_HidesSecretsAndPreservesBusinessData(string type, string payload)
    {
        var result = NetworkLogSanitizer.Body(Encoding.UTF8.GetBytes(payload), type);
        Assert.DoesNotContain("secret", result);
        Assert.Contains("A001", result);
    }

    [Fact]
    public void Html_HidesSessionTokenAndEmbeddedAuthorizationUrl()
    {
        var body = "<script>const access='secret'; const link='https://a.test/?code=secret';</script>";
        Assert.DoesNotContain("secret", NetworkLogSanitizer.Body(Encoding.UTF8.GetBytes(body), "text/html", ["secret"]));
    }

    [Theory]
    [InlineData(65535, "完整")]
    [InlineData(65536, "完整")]
    [InlineData(65537, "超过上限")]
    public void Capture_EnforcesByteLimit(int length, string state)
    {
        var body = new NetworkBodyCapture(() => true);
        body.Append(Encoding.UTF8.GetBytes(new string('a', length)));
        body.Complete();
        Assert.Contains(state, body.Render("text/plain"));
    }

    [Theory]
    [InlineData("application/json", "{\"password\":\"secret")]
    [InlineData("application/xml", "<!DOCTYPE x [<!ENTITY a SYSTEM 'file:///secret'>]><x>&a;</x>")]
    [InlineData("application/json; charset=no-such-encoding", "secret")]
    public void Capture_UnparseableContentNeverFallsBackToRawText(string type, string payload)
    {
        var body = new NetworkBodyCapture(() => true);
        body.Append(Encoding.UTF8.GetBytes(payload));
        body.Complete();
        var result = body.Render(type);
        Assert.Contains("无法安全脱敏", result);
        Assert.DoesNotContain("secret", result);
    }

    [Fact]
    public void Capture_DecodesSplitChineseOnlyAfterCompletion()
    {
        var bytes = Encoding.UTF8.GetBytes("中文正文");
        var body = new NetworkBodyCapture(() => true);
        foreach (var value in bytes) body.Append([value]);
        body.Complete();
        Assert.Contains("中文正文", body.Render("text/plain"));
    }

    [Fact]
    public void Capture_InvalidUtf8IsOmitted()
    {
        var body = new NetworkBodyCapture(() => true);
        body.Append([0xe4, 0xb8]);
        body.Complete();
        Assert.Contains("无法安全脱敏", body.Render("text/plain"));
    }

    [Fact]
    public void Capture_PartialAndBinaryContentIsOmitted()
    {
        var partial = new NetworkBodyCapture(() => true);
        partial.Append("secret"u8);
        Assert.DoesNotContain("secret", partial.Render("text/plain"));
        var binary = new NetworkBodyCapture(() => true);
        binary.Append("secret"u8);
        binary.Complete();
        Assert.Contains("二进制", binary.Render("image/png"));
    }
}
