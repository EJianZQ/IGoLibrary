using System.IO.Compression;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Infrastructure.Diagnostics;

namespace IGoLibrary.Ex.Tests;

public sealed class DiagnosticExportServiceTests
{
    [Fact]
    public async Task ExportAsync_WritesDiagnosticZipWithoutSensitiveValues()
    {
        var root = Path.Combine(Path.GetTempPath(), "igolibrary-diagnostics-tests", Guid.NewGuid().ToString("N"));
        var logs = Path.Combine(root, "logs");
        Directory.CreateDirectory(logs);
        await File.WriteAllTextAsync(
            Path.Combine(logs, "app.log"),
            "Authorization=secret-cookie; BotToken=telegram-secret; deviceKey=bark-secret; password=smtp-secret");
        var settingsService = new FakeSettingsService(AppSettings.Default with
        {
            Notifications = AppSettings.Default.Notifications with
            {
                TaskEventAlerts = new TaskEventAlertSettings(
                    EmailAlertChannelSettings.Default with
                    {
                        Enabled = true,
                        SmtpHost = "smtp.example.com",
                        Username = "user@example.com",
                        Password = "smtp-secret",
                        FromAddress = "user@example.com",
                        ToAddress = "me@example.com"
                    },
                    LocalDesktopAlertSettings.Default,
                    TelegramAlertChannelSettings.Default with
                    {
                        Enabled = true,
                        BotToken = "telegram-secret",
                        ChatId = "123"
                    },
                    BarkAlertChannelSettings.Default with
                    {
                        Enabled = true,
                        DeviceKey = "bark-secret"
                    })
            }
        });
        IDiagnosticExportService service = new DiagnosticExportService(
            settingsService,
            root,
            logs,
            () => new DateTimeOffset(2026, 6, 15, 10, 30, 0, TimeSpan.Zero));

        var result = await service.ExportAsync();

        Assert.True(File.Exists(result.FilePath));
        using var archive = ZipFile.OpenRead(result.FilePath);
        var combined = string.Join(
            "\n",
            archive.Entries
                .Where(entry => !string.IsNullOrEmpty(entry.Name))
                .Select(entry =>
                {
                    using var reader = new StreamReader(entry.Open());
                    return reader.ReadToEnd();
                }));
        Assert.DoesNotContain("secret-cookie", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("telegram-secret", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("bark-secret", combined, StringComparison.Ordinal);
        Assert.DoesNotContain("smtp-secret", combined, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", combined, StringComparison.Ordinal);
    }
}
