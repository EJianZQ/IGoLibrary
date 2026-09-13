using IGoLibrary.Ex.Application.Logging;
using System.Net;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Infrastructure.Api;
using IGoLibrary.Ex.Infrastructure.Logging;
using IGoLibrary.Ex.Infrastructure.Notifications;
using IGoLibrary.Ex.Infrastructure.Persistence;
using IGoLibrary.Ex.Infrastructure.Protocol;
using IGoLibrary.Ex.Infrastructure.Security;
using IGoLibrary.Ex.Infrastructure.DataTransfer;
using IGoLibrary.Ex.Infrastructure.Updates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IGoLibrary.Ex.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.TryAddSingleton<StorageLocationManager>();
        services.TryAddSingleton<IStorageLocationService>(serviceProvider =>
            serviceProvider.GetRequiredService<StorageLocationManager>());
        services.TryAddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<StorageLocationManager>().Current);
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<PersistentDataChangeTracker>();
        services.AddSingleton<IPersistentDataChangeTracker>(serviceProvider =>
            serviceProvider.GetRequiredService<PersistentDataChangeTracker>());
        services.AddSingleton<IAppDataInitializer, SqliteAppDataInitializer>();
        services.TryAddSingleton<IAppLogWriter, AppLogFileWriter>();
        services.TryAddSingleton<NetworkLogState>();
        services.TryAddSingleton<IAppLogRuntimeController, AppLogRuntimeController>();
        services.TryAddSingleton<NetworkTrafficLogger>();
        services.AddSingleton<AppTraceListener>();
        services.AddSingleton<TraceListenerRegistrar>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        services.AddSingleton<IFavoritesRepository, SqliteFavoritesRepository>();
        services.AddSingleton<ISeatLabelRepository, SqliteSeatLabelRepository>();
        services.AddSingleton<ITaskLaunchHistoryRepository, SqliteTaskLaunchHistoryRepository>();
        services.AddSingleton<IProtocolTemplateStore, DefaultProtocolTemplateStore>();
        services.AddSingleton<ICredentialStore>(serviceProvider =>
            PlatformCredentialStore.CreateDefault(
                serviceProvider.GetRequiredService<IPersistentDataChangeTracker>()));
        services.AddSingleton<IBackupSecretStore>(serviceProvider =>
            new PlatformBackupSecretStore(
                serviceProvider.GetRequiredService<IPersistentDataChangeTracker>()));
        services.AddSingleton<IDataBackupService, DataBackupService>();
        services.AddSingleton<IPersistentDataFingerprintProvider, PersistentDataFingerprintProvider>();
        services.AddSingleton<IBackupRestoreStartupService, BackupRestoreStartupService>();
        services.AddSingleton<WebDavClient>();
        services.AddSingleton<IWebDavSyncService, WebDavSyncService>();
        services.AddSingleton<ISmtpTransportClientFactory, MailKitSmtpTransportClientFactory>();
        services.AddSingleton<IEmailAlertSender, SmtpEmailAlertSender>();
        services.AddHttpClient<IBarkAlertSender, BarkAlertSender>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).AddHttpMessageHandler(sp => new NetworkLoggingHandler(sp.GetRequiredService<NetworkTrafficLogger>(), "BarkAlertSender"));
        services.AddHttpClient<IWxPusherAlertSender, WxPusherAlertSender>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).AddHttpMessageHandler(sp => new NetworkLoggingHandler(sp.GetRequiredService<NetworkTrafficLogger>(), "WxPusherAlertSender"));
        services.AddHttpClient<IServerChanAlertSender, ServerChanAlertSender>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).AddHttpMessageHandler(sp => new NetworkLoggingHandler(sp.GetRequiredService<NetworkTrafficLogger>(), "ServerChanAlertSender"));
        services.AddHttpClient<ITelegramAlertSender, TelegramAlertSender>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
        }).AddHttpMessageHandler(sp => new NetworkLoggingHandler(sp.GetRequiredService<NetworkTrafficLogger>(), "TelegramAlertSender"));
        services.AddHttpClient<IGitHubReleaseClient, GitHubReleaseClient>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("IGoLibrary-Ex");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            client.DefaultRequestHeaders.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
        }).AddHttpMessageHandler(sp => new NetworkLoggingHandler(sp.GetRequiredService<NetworkTrafficLogger>(), "GitHubReleaseClient"));
        services.AddHttpClient<IReleaseAssetDownloader, GitHubReleaseAssetDownloader>(client =>
        {
            client.Timeout = Timeout.InfiniteTimeSpan;
            client.DefaultRequestHeaders.UserAgent.ParseAdd("IGoLibrary-Ex");
            client.DefaultRequestHeaders.Accept.ParseAdd("application/octet-stream");
        }).AddHttpMessageHandler(sp => new NetworkLoggingHandler(sp.GetRequiredService<NetworkTrafficLogger>(), "GitHubReleaseAssetDownloader")).ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
        {
            ConnectTimeout = TimeSpan.FromSeconds(30)
        });

        services.AddSingleton<TraceIntRequestPolicy>();
        services.AddSingleton<ITraceIntCookieHttpClient, RestSharpTraceIntCookieHttpClient>();
        services.AddSingleton<TraceIntCookieTransport>();
        services.AddSingleton<TraceIntTomorrowReservationQueueTransport>();
        services.AddHttpClient<TraceIntGraphQlTransport>(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            }).AddHttpMessageHandler(sp => new NetworkLoggingHandler(sp.GetRequiredService<NetworkTrafficLogger>(), "TraceIntGraphQlTransport"))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                UseCookies = false
            });
        services.AddHttpClient<TraceIntRemoteCheckInTransport>(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
            }).AddHttpMessageHandler(sp => new NetworkLoggingHandler(sp.GetRequiredService<NetworkTrafficLogger>(), "TraceIntRemoteCheckInTransport"))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = DecompressionMethods.All,
                UseCookies = false
            });
        services.AddTransient<IRemoteCheckInApiClient, TraceIntRemoteCheckInApiClient>();
        services.AddTransient<ITraceIntApiClient, TraceIntApiClient>();

        return services;
    }
}
