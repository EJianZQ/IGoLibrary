using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record CookieExpiryEmailAlertSettings(
    bool Enabled,
    string SmtpHost,
    int Port,
    EmailSecurityMode SecurityMode,
    string Username,
    string Password,
    string FromAddress,
    string ToAddress)
{
    public static CookieExpiryEmailAlertSettings Default { get; } = new(
        Enabled: false,
        SmtpHost: string.Empty,
        Port: 587,
        SecurityMode: EmailSecurityMode.Tls,
        Username: string.Empty,
        Password: string.Empty,
        FromAddress: string.Empty,
        ToAddress: string.Empty);
}
