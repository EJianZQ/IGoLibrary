using System.IO.Pipes;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace IGoLibrary.Ex.Desktop.Services;

internal sealed class MihomoControllerClient : IMihomoControllerClient
{
    public async Task ReloadAsync(
        MihomoConfiguration configuration,
        string configurationPath,
        CancellationToken cancellationToken = default)
    {
        using var handler = CreateHandler(configuration.Controller);
        using var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        var endpoint = configuration.Controller switch
        {
            MihomoControllerEndpoint.Http http => new Uri(http.BaseUri, "configs?force=true"),
            MihomoControllerEndpoint.WindowsNamedPipe => new Uri("http://localhost/configs?force=true"),
            _ => throw new InvalidOperationException("不支持的 Mihomo 控制接口类型")
        };
        var payload = JsonSerializer.Serialize(new { path = configurationPath, payload = string.Empty });
        using var request = new HttpRequestMessage(HttpMethod.Put, endpoint)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json"),
            Version = HttpVersion.Version11,
            VersionPolicy = HttpVersionPolicy.RequestVersionExact
        };
        request.Headers.ExpectContinue = false;
        if (configuration.Controller is MihomoControllerEndpoint.Http &&
            !string.IsNullOrEmpty(configuration.Secret))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", configuration.Secret);
        }

        using var response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Mihomo 控制接口拒绝加载临时配置（HTTP {(int)response.StatusCode}）");
        }
    }

    private static SocketsHttpHandler CreateHandler(MihomoControllerEndpoint controller)
    {
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(3)
        };
        if (controller is MihomoControllerEndpoint.WindowsNamedPipe pipe)
        {
            handler.ConnectCallback = (_, cancellationToken) =>
                ConnectNamedPipeAsync(pipe.PipeName, cancellationToken);
        }

        return handler;
    }

    private static async ValueTask<Stream> ConnectNamedPipeAsync(
        string pipeName,
        CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Mihomo Windows 命名管道只能在 Windows 上使用");
        }

        var stream = new NamedPipeClientStream(
            ".",
            pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        try
        {
            await stream.ConnectAsync(cancellationToken);
            return stream;
        }
        catch
        {
            await stream.DisposeAsync();
            throw;
        }
    }
}
