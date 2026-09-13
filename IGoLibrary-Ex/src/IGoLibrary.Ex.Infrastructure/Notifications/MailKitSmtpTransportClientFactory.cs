using IGoLibrary.Ex.Infrastructure.Logging;
namespace IGoLibrary.Ex.Infrastructure.Notifications;

internal sealed class MailKitSmtpTransportClientFactory(NetworkTrafficLogger? networkLogger = null) : ISmtpTransportClientFactory
{
    public ISmtpTransportClient Create() => new MailKitSmtpTransportClient(networkLogger);
}
