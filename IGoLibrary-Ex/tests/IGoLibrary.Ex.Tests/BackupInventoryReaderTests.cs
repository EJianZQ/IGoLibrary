using System.Text.Json;
using IGoLibrary.Ex.Infrastructure.DataTransfer;

namespace IGoLibrary.Ex.Tests;

public sealed class BackupInventoryReaderTests
{
    [Fact]
    public void UiSafeSummary_IncludesTaskSleepPreventionSetting()
    {
        using var document = JsonDocument.Parse(
            "{\"minimizeToTray\":true,\"preventSystemSleepWhileTasksActive\":false,\"launchOnStartup\":true}");

        var summary = BackupInventoryReader.BuildSafeSettingsSummary("ui", document.RootElement);

        Assert.NotNull(summary);
        Assert.Contains("preventSystemSleepWhileTasksActive=false", summary, StringComparison.Ordinal);
    }
}
