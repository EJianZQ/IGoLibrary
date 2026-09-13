using System.Net;
using IGoLibrary.Ex.Desktop;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Infrastructure.Api;
using IGoLibrary.Ex.Infrastructure.DataTransfer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class NetworkLoggingDynamicClientTests
{
    [Fact]
    public async Task RestSharpLogsThroughProductionAdapterAndKeepsCookies()
    {
        await using var server = await StartServerAsync(async context =>
        {
            context.Response.ContentType = "text/plain; charset=utf-8";
            context.Response.Cookies.Append("a", "cookie-secret");
            context.Response.Cookies.Append("b", "second-cookie");
            await context.Response.WriteAsync("login-response");
        });
        var logs = new NetworkLogTestContext();
        var transport = new RestSharpTraceIntCookieHttpClient(logs.Logger);
        var result = await transport.ExecuteGetAsync(server.Urls.Single() + "/?code=private-code", default);
        Assert.Equal("login-response", result.Response.Content);
        Assert.Equal(2, result.Cookies!.Count);
        Assert.Contains("login-response", logs.Writer.Text);
        Assert.DoesNotContain("cookie-secret", logs.Writer.Text);
        Assert.DoesNotContain("private-code", logs.Writer.Text);
    }

    [Fact]
    public async Task WebDavDynamicClientLogsPutGetDeleteWithoutChangingProbe()
    {
        byte[] stored = [];
        await using var server = await StartServerAsync(async context =>
        {
            if (context.Request.Method == "PUT")
            {
                using var data = new MemoryStream();
                await context.Request.Body.CopyToAsync(data);
                stored = data.ToArray();
            }
            else if (context.Request.Method == "GET")
            {
                context.Response.ContentType = "text/plain";
                await context.Response.Body.WriteAsync(stored);
            }
        });
        var logs = new NetworkLogTestContext();
        var transport = new WebDavClient(TimeProvider.System, NullLogger<WebDavClient>.Instance, logs.Logger);
        using var client = transport.CreateHttpClient("", "", WebDavTlsVerifyMode.Verify);
        await transport.ProbeWriteAsync(client, new Uri(server.Urls.Single() + "/backup.bin"), default);
        foreach (var method in new[] { "PUT", "GET", "DELETE" }) Assert.Contains($"方法={method}", logs.Writer.Text);
        Assert.Contains("IGoLibrary-Ex", logs.Writer.Text);
    }

    [Fact]
    public async Task HealthProbeAndMihomoUseDynamicLoggingHandlers()
    {
        await using var server = await StartServerAsync(context =>
        {
            context.Response.StatusCode = 204;
            return Task.CompletedTask;
        });
        var logs = new NetworkLogTestContext();
        using var probe = new CloudflareTunnelHealthProbeFactory(logs.Logger).Create(null);
        Assert.True((await probe.ProbeAsync(new Uri(server.Urls.Single()), TimeSpan.FromSeconds(5))).IsHealthy);
        var controller = new MihomoControllerClient(logs.Logger);
        await controller.ReloadAsync(new MihomoConfiguration("test", ".", "config.yaml",
            new MihomoControllerEndpoint.Http(new Uri(server.Urls.Single())), "controller-secret"), "config.yaml");
        Assert.Contains("Tunnel 健康检查", logs.Writer.Text);
        Assert.Contains("Mihomo", logs.Writer.Text);
        Assert.Contains("config.yaml", logs.Writer.Text);
        Assert.DoesNotContain("controller-secret", logs.Writer.Text);
    }

    [Fact]
    public async Task AvatarNamedClientIsRegisteredInProductionHost()
    {
        var logs = new NetworkLogTestWriter();
        using var host = HostBuilderFactory.Create([], logs).ConfigureServices(services =>
            services.AddHttpClient("ProjectAvatar").ConfigurePrimaryHttpMessageHandler(() =>
                new NetworkTestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new StringContent("avatar-response") })))).Build();
        host.Services.GetRequiredService<IGoLibrary.Ex.Application.Logging.NetworkLogState>().Apply(true);
        using var client = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient("ProjectAvatar");
        using var response = await client.GetAsync("https://avatar.test");
        Assert.Contains("作者头像", logs.Text);
        Assert.Contains("avatar-response", logs.Text);
    }

    private static async Task<WebApplication> StartServerAsync(RequestDelegate handler)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        app.Run(handler);
        try { await app.StartAsync(); return app; }
        catch { await app.DisposeAsync(); throw; }
    }
}
