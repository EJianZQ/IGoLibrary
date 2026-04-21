using System.Net;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Infrastructure.Api;
using IGoLibrary.Ex.Infrastructure.Logging;
using IGoLibrary.Ex.Infrastructure.Notifications;
using IGoLibrary.Ex.Infrastructure.Persistence;
using IGoLibrary.Ex.Infrastructure.Protocol;
using IGoLibrary.Ex.Infrastructure.Security;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IGoLibrary.Ex.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<IAppDataInitializer, SqliteAppDataInitializer>();
        services.TryAddSingleton<IAppLogWriter, AppLogFileWriter>();
        services.AddSingleton<AppTraceListener>();
        services.AddSingleton<TraceListenerRegistrar>();
        services.AddSingleton<ISettingsRepository, SqliteSettingsRepository>();
        services.AddSingleton<IFavoritesRepository, SqliteFavoritesRepository>();
        services.AddSingleton<IProtocolTemplateStore, DefaultProtocolTemplateStore>();
        services.AddSingleton<ICredentialStore>(_ => PlatformCredentialStore.CreateDefault());
        services.AddSingleton<ISmtpTransportClientFactory, MailKitSmtpTransportClientFactory>();
        services.AddSingleton<IEmailAlertSender, SmtpEmailAlertSender>();

        services.AddHttpClient<ITraceIntApiClient, TraceIntApiClient>(client =>
            {
                client.Timeout = Timeout.InfiniteTimeSpan;
                client.DefaultRequestHeaders.TryAddWithoutValidation(
                    "User-Agent",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
            })
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.All,
                UseCookies = false
            });

        return services;
    }
}
