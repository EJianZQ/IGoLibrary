using System.Net;
using IGoLibrary.Ex.Application;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Logging;
using IGoLibrary.Ex.Infrastructure;
using IGoLibrary.Ex.Infrastructure.Api;
using IGoLibrary.Ex.Infrastructure.DataTransfer;
using IGoLibrary.Ex.Infrastructure.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Tests;

public sealed class NetworkLoggingRegistrationTests
{
    [Theory]
    [InlineData(nameof(IBarkAlertSender))]
    [InlineData(nameof(IWxPusherAlertSender))]
    [InlineData(nameof(IServerChanAlertSender))]
    [InlineData(nameof(ITelegramAlertSender))]
    [InlineData(nameof(IGitHubReleaseClient))]
    [InlineData(nameof(IReleaseAssetDownloader))]
    [InlineData(nameof(TraceIntGraphQlTransport))]
    [InlineData(nameof(TraceIntRemoteCheckInTransport))]
    public async Task EveryRegisteredClientUsesNetworkHandler(string name)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        var logs = new NetworkLogTestWriter();
        services.AddSingleton<IAppLogWriter>(logs);
        services.AddInfrastructure();
        services.AddHttpClient(name).ConfigurePrimaryHttpMessageHandler(() =>
            new NetworkTestHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("registered-response") })));
        using var provider = services.BuildServiceProvider();
        provider.GetRequiredService<NetworkLogState>().Apply(true);
        using var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(name);
        using var response = await client.GetAsync("https://example.test/path");
        Assert.Contains("registered-response", logs.Text);
        Assert.Single(logs.Entries, e => e.Message.Contains("开始；"));
    }

    [Fact]
    public void DynamicWebDavClientPreservesTransportPolicyAndAcceptsLogger()
    {
        var logs = new NetworkLogTestContext();
        var webDav = new WebDavClient(TimeProvider.System, Microsoft.Extensions.Logging.Abstractions.NullLogger<WebDavClient>.Instance, logs.Logger);
        using var client = webDav.CreateHttpClient("user", "secret", WebDavTlsVerifyMode.Verify);
        Assert.Equal(WebDavClient.RequestTimeout, client.Timeout);
        using var handler = WebDavClient.CreateHandler("user", "secret", WebDavTlsVerifyMode.Verify);
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseCookies);
    }
}
