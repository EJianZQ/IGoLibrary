using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Application.Services;
using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class ClashMihomoCompatibilityTests
{
    [Theory]
    [InlineData("external-controller: 127.0.0.1:9090\nsecret: test-secret", "http://127.0.0.1:9090/")]
    [InlineData("external-controller: 'localhost:9097'\nsecret: ''", "http://localhost:9097/")]
    [InlineData("external-controller: \"[::1]:9099\"\nsecret: \"token\"", "http://[::1]:9099/")]
    [InlineData("external-controller: 0.0.0.0:9090", "http://127.0.0.1:9090/")]
    [InlineData("external-controller: :9091", "http://127.0.0.1:9091/")]
    public void Locator_ReadsOnlyLoopbackController(string yaml, string expectedUri)
    {
        var success = ClashMihomoConfigurationLocator.TryReadController(
            yaml,
            out var uri,
            out var secret);

        Assert.True(success);
        var http = Assert.IsType<MihomoControllerEndpoint.Http>(uri);
        Assert.Equal(new Uri(expectedUri), http.BaseUri);
        Assert.NotNull(secret);
    }

    [Fact]
    public void Locator_UsesWindowsNamedPipeWhenRestControllerIsDisabled()
    {
        const string yaml =
            "external-controller: ''\nexternal-controller-pipe: \\\\.\\pipe\\verge-mihomo\nsecret: ignored";

        var success = ClashMihomoConfigurationLocator.TryReadController(
            yaml,
            out var endpoint,
            out var secret);

        Assert.True(success);
        var pipe = Assert.IsType<MihomoControllerEndpoint.WindowsNamedPipe>(endpoint);
        Assert.Equal("verge-mihomo", pipe.PipeName);
        Assert.Equal(string.Empty, secret);
    }

    [Fact]
    public void Locator_PrefersRestControllerWhenBothTransportsAreEnabled()
    {
        const string yaml =
            "external-controller: 127.0.0.1:9097\nexternal-controller-pipe: \\\\.\\pipe\\verge-mihomo\nsecret: token";

        Assert.True(ClashMihomoConfigurationLocator.TryReadController(
            yaml,
            out var endpoint,
            out var secret));

        Assert.IsType<MihomoControllerEndpoint.Http>(endpoint);
        Assert.Equal("token", secret);
    }

    [Theory]
    [InlineData("external-controller: 192.168.1.5:9090")]
    [InlineData("external-controller: https://127.0.0.1:9090")]
    [InlineData("external-controller-pipe: \\\\.\\pipe\\folder\\mihomo")]
    [InlineData("external-controller-pipe: \\\\remote\\pipe\\mihomo")]
    public void Locator_RejectsRemoteOrUnsupportedController(string yaml)
    {
        Assert.False(ClashMihomoConfigurationLocator.TryReadController(
            yaml,
            out _,
            out _));
    }

    [Fact]
    public void RuleInjection_PrependsProcessAndDomainRulesWithCustomPolicy()
    {
        const string source = "mode: rule\r\nrules:\r\n- MATCH,Existing Group\r\n";

        var result = ClashMihomoCompatibilityService.AddCompatibilityRules(
            source,
            "Cloudflare 专线");

        var processRule = result.IndexOf(
            "PROCESS-NAME,cloudflared.exe,Cloudflare 专线",
            StringComparison.Ordinal);
        var existingRule = result.IndexOf("MATCH,Existing Group", StringComparison.Ordinal);
        Assert.True(processRule > 0);
        Assert.True(existingRule > processRule);
        Assert.Contains("DOMAIN,api.trycloudflare.com,Cloudflare 专线", result, StringComparison.Ordinal);
        Assert.Contains("DOMAIN-SUFFIX,argotunnel.com,Cloudflare 专线", result, StringComparison.Ordinal);
        Assert.DoesNotContain("\nrules:\n", result, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompatibilityService_ReferenceCountsAndRestoresOriginalConfiguration()
    {
        var root = Path.Combine(Path.GetTempPath(), "IGoLibrary-Ex-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "config.yaml");
        await File.WriteAllTextAsync(
            sourcePath,
            "external-controller: 127.0.0.1:9090\nsecret: token\nrules:\n- MATCH,DIRECT\n");
        try
        {
            var configuration = new MihomoConfiguration(
                "Test Mihomo",
                root,
                sourcePath,
                new MihomoControllerEndpoint.Http(new Uri("http://127.0.0.1:9090/")),
                "token");
            var locator = new FakeLocator(configuration);
            var controller = new FakeControllerClient();
            var service = new ClashMihomoCompatibilityService(
                locator,
                controller,
                new ActivityLogService(),
                NullLogger<ClashMihomoCompatibilityService>.Instance);
            var options = new ClashMihomoCompatibilityOptions(true, sourcePath, "DIRECT");

            var first = Assert.IsAssignableFrom<IAsyncDisposable>(await service.AcquireAsync(options));
            var second = Assert.IsAssignableFrom<IAsyncDisposable>(await service.AcquireAsync(options));

            Assert.Single(controller.LoadedPaths);
            var temporaryPath = controller.LoadedPaths[0];
            Assert.NotEqual(sourcePath, temporaryPath);
            Assert.True(File.Exists(temporaryPath));
            Assert.Contains("cloudflared.exe", await File.ReadAllTextAsync(temporaryPath), StringComparison.Ordinal);

            await first.DisposeAsync();
            Assert.Single(controller.LoadedPaths);
            await second.DisposeAsync();

            Assert.Equal([temporaryPath, sourcePath], controller.LoadedPaths);
            Assert.False(File.Exists(temporaryPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CompatibilityService_DifferentPolicyCannotReplaceActiveRulesSilently()
    {
        var root = Path.Combine(Path.GetTempPath(), "IGoLibrary-Ex-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "config.yaml");
        await File.WriteAllTextAsync(
            sourcePath,
            "external-controller: 127.0.0.1:9090\nrules:\n- MATCH,DIRECT\n");
        try
        {
            var configuration = new MihomoConfiguration(
                "Test Mihomo",
                root,
                sourcePath,
                new MihomoControllerEndpoint.Http(new Uri("http://127.0.0.1:9090/")),
                string.Empty);
            var service = new ClashMihomoCompatibilityService(
                new FakeLocator(configuration),
                new FakeControllerClient(),
                new ActivityLogService(),
                NullLogger<ClashMihomoCompatibilityService>.Instance);
            await using var lease = Assert.IsAssignableFrom<IAsyncDisposable>(await service.AcquireAsync(
                new ClashMihomoCompatibilityOptions(true, sourcePath, "DIRECT")));

            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.AcquireAsync(
                new ClashMihomoCompatibilityOptions(true, sourcePath, "Proxy Group")));

            Assert.Contains("先切换到本机局域网", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CompatibilityService_RetriesFailedRestoreBeforeApplyingDifferentPolicy()
    {
        var root = Path.Combine(Path.GetTempPath(), "IGoLibrary-Ex-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var sourcePath = Path.Combine(root, "config.yaml");
        await File.WriteAllTextAsync(
            sourcePath,
            "external-controller: 127.0.0.1:9090\nrules:\n- MATCH,DIRECT\n");
        try
        {
            var configuration = new MihomoConfiguration(
                "Test Mihomo",
                root,
                sourcePath,
                new MihomoControllerEndpoint.Http(new Uri("http://127.0.0.1:9090/")),
                string.Empty);
            var controller = new FakeControllerClient
            {
                FailOnCall = 2
            };
            var service = new ClashMihomoCompatibilityService(
                new FakeLocator(configuration),
                controller,
                new ActivityLogService(),
                NullLogger<ClashMihomoCompatibilityService>.Instance);
            var first = Assert.IsAssignableFrom<IAsyncDisposable>(await service.AcquireAsync(
                new ClashMihomoCompatibilityOptions(true, sourcePath, "DIRECT")));
            var firstTemporaryPath = Assert.Single(controller.LoadedPaths);

            await first.DisposeAsync();

            Assert.True(File.Exists(firstTemporaryPath));
            Assert.Equal([firstTemporaryPath, sourcePath], controller.LoadedPaths);
            controller.FailOnCall = null;

            var second = Assert.IsAssignableFrom<IAsyncDisposable>(await service.AcquireAsync(
                new ClashMihomoCompatibilityOptions(true, sourcePath, "Proxy Group")));
            var secondTemporaryPath = controller.LoadedPaths[^1];

            Assert.False(File.Exists(firstTemporaryPath));
            Assert.NotEqual(firstTemporaryPath, secondTemporaryPath);
            Assert.Equal(
                [firstTemporaryPath, sourcePath, sourcePath, secondTemporaryPath],
                controller.LoadedPaths);

            await second.DisposeAsync();

            Assert.Equal(sourcePath, controller.LoadedPaths[^1]);
            Assert.False(File.Exists(secondTemporaryPath));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task ControllerClient_ReloadsConfigurationThroughWindowsNamedPipe()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var pipeName = $"igolibrary-mihomo-test-{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var requestSource = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var serverTask = ServeSingleRequestAsync(server, requestSource);
        var configuration = new MihomoConfiguration(
            "Test Mihomo Pipe",
            Path.GetTempPath(),
            Path.Combine(Path.GetTempPath(), "config.yaml"),
            new MihomoControllerEndpoint.WindowsNamedPipe(pipeName),
            "must-not-be-sent");
        var client = new MihomoControllerClient();
        var targetPath = Path.Combine(Path.GetTempPath(), "temporary config.yaml");

        await client.ReloadAsync(configuration, targetPath);
        await serverTask;
        var request = await requestSource.Task;

        Assert.StartsWith("PUT /configs?force=true HTTP/1.1\r\n", request, StringComparison.Ordinal);
        Assert.Contains("\"path\":", request, StringComparison.Ordinal);
        Assert.Contains("temporary config.yaml", request, StringComparison.Ordinal);
        Assert.DoesNotContain("Authorization:", request, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("must-not-be-sent", request, StringComparison.Ordinal);
    }

    private static async Task ServeSingleRequestAsync(
        NamedPipeServerStream server,
        TaskCompletionSource<string> requestSource)
    {
        await server.WaitForConnectionAsync();
        var buffer = new byte[16 * 1024];
        var content = new StringBuilder();
        var contentLength = 0;
        while (true)
        {
            var count = await server.ReadAsync(buffer);
            if (count == 0)
            {
                break;
            }

            content.Append(Encoding.UTF8.GetString(buffer, 0, count));
            var current = content.ToString();
            var headerEnd = current.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd < 0)
            {
                continue;
            }

            if (contentLength == 0)
            {
                var lengthLine = current[..headerEnd]
                    .Split("\r\n", StringSplitOptions.None)
                    .First(static line => line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase));
                contentLength = int.Parse(lengthLine.Split(':', 2)[1].Trim());
            }

            if (Encoding.UTF8.GetByteCount(current[(headerEnd + 4)..]) >= contentLength)
            {
                break;
            }
        }

        requestSource.TrySetResult(content.ToString());
        var response = Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nConnection: close\r\nContent-Length: 0\r\n\r\n");
        await server.WriteAsync(response);
        await server.FlushAsync();
    }

    private sealed class FakeLocator(params MihomoConfiguration[] configurations)
        : IClashMihomoConfigurationLocator
    {
        public Task<IReadOnlyList<MihomoConfiguration>> FindAsync(
            string configPath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MihomoConfiguration>>(configurations);
        }
    }

    private sealed class FakeControllerClient : IMihomoControllerClient
    {
        public List<string> LoadedPaths { get; } = [];

        public int? FailOnCall { get; set; }

        public Task ReloadAsync(
            MihomoConfiguration configuration,
            string configurationPath,
            CancellationToken cancellationToken = default)
        {
            LoadedPaths.Add(configurationPath);
            if (LoadedPaths.Count == FailOnCall)
            {
                throw new IOException("controller unavailable");
            }

            return Task.CompletedTask;
        }
    }
}
