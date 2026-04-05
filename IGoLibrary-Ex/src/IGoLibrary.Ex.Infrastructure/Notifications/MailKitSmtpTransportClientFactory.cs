namespace IGoLibrary.Ex.Infrastructure.Notifications;

internal sealed class MailKitSmtpTransportClientFactory : ISmtpTransportClientFactory
{
    public ISmtpTransportClient Create() => new MailKitSmtpTransportClient();
}
