namespace IGoLibrary.Ex.Updater.Tests;

public sealed class UpdaterLogTests : IDisposable
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-UpdaterLogTests",
        Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_tempDirectory))
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void Write_RedactsSensitiveValues_PreservesStack_AndConstrainsFileName()
    {
        var log = new UpdaterLog(_tempDirectory, "../../transaction", "worker:admin");

        log.Info(
            @"下载 https://example.test/package?signature=query-secret | https://alice:uri-secret@example.test/file | Authorization: Bearer auth-secret | Proxy-Authorization: Basic proxy-secret | Bearer loose-secret | token=named-secret | {""refresh_token"":""json-secret with spaces""} | alice@example.com | C:\Users\Alice Doe\Downloads\package.zip | /Users/alice/package.zip");
        log.Error(
            "更新安装失败。",
            CaptureSensitiveException());

        var path = Assert.Single(Directory.GetFiles(_tempDirectory, "*.log"));
        Assert.Matches(
            @"^\d{8}-transaction-workeradmin\.log$",
            Path.GetFileName(path));

        var lines = File.ReadAllLines(path);
        Assert.Equal(2, lines.Length);
        var content = string.Join(Environment.NewLine, lines);
        Assert.DoesNotContain("query-secret", content);
        Assert.DoesNotContain("uri-secret", content);
        Assert.DoesNotContain("auth-secret", content);
        Assert.DoesNotContain("proxy-secret", content);
        Assert.DoesNotContain("loose-secret", content);
        Assert.DoesNotContain("named-secret", content);
        Assert.DoesNotContain("json-secret", content);
        Assert.DoesNotContain("inner-secret", content);
        Assert.DoesNotContain("alice@example.com", content);
        Assert.DoesNotContain(@"\Alice Doe\", content);
        Assert.DoesNotContain("/Users/alice/", content);
        Assert.DoesNotContain("/home/alice/", content);
        Assert.Contains("<redacted>", content);
        Assert.Contains("***@example.com", content);
        Assert.Contains(@"%USERPROFILE%\Downloads\package.zip", content);
        Assert.Contains("/Users/<user>/package.zip", content);
        Assert.Contains("/home/<user>/state.json", content);
        Assert.Contains(nameof(ThrowSensitiveException), content);
        Assert.Contains("IOException", content);
    }

    private static Exception CaptureSensitiveException()
    {
        try
        {
            ThrowSensitiveException();
            throw new InvalidOperationException("不可达");
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    private static void ThrowSensitiveException()
    {
        throw new IOException("token=inner-secret | /home/alice/state.json");
    }
}
