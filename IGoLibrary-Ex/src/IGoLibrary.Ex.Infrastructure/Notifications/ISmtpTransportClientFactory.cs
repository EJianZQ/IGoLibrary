namespace IGoLibrary.Ex.Infrastructure.Notifications;

internal interface ISmtpTransportClientFactory
{
    ISmtpTransportClient Create();
}
