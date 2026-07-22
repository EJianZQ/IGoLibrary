using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using IGoLibrary.Ex.Application.Backup;
using IGoLibrary.Ex.Desktop;

namespace IGoLibrary.Ex.Tests;

[Collection(NonParallelTestCollection.Name)]
public sealed class BackupWindowsTests
{
    [AvaloniaFact]
    public void PasswordWindow_AlwaysMasksPasswordAndConfirmation()
    {
        var window = new BackupPasswordWindow("密码", "说明", requireConfirmation: true);

        Assert.Equal('●', window.PasswordBox.PasswordChar);
        Assert.Equal('●', window.ConfirmationBox?.PasswordChar);
        Assert.False(window.PasswordBox.RevealPassword);
        Assert.False(window.ConfirmationBox?.RevealPassword);
    }

    [AvaloniaFact]
    public async Task ComparisonWindow_RequiresExplicitOverwriteAcknowledgement()
    {
        var prepared = new PreparedBackup(
            "preview",
            "backup.igobackup",
            new BackupManifest(
                1,
                "1.0.0",
                1,
                DateTimeOffset.Parse("2026-07-18T08:00:00Z"),
                "windows",
                1024,
                new string('A', 64),
                2,
                new string('B', 64),
                new string('C', 64),
                new BackupDataSummary(1, 1, 0, 0, 0, true, false, false)),
            new BackupComparison(
                0,
                0,
                1,
                0,
                [new BackupDifferenceItem(
                    "登录会话",
                    BackupDifferenceKind.Changed,
                    "已配置（内容已隐藏）",
                    "已配置（内容已隐藏）",
                    true)]),
            "operation");
        var owner = new Window();
        owner.Show();
        try
        {
            var window = new BackupComparisonWindow(prepared);
            var result = window.ShowDialog<bool>(owner);
            Dispatcher.UIThread.RunJobs();
            Assert.False(window.RestoreButton.IsEnabled);

            window.UnderstoodCheckBox.IsChecked = true;
            Assert.True(window.RestoreButton.IsEnabled);
            window.RestoreButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

            Assert.True(await result);
        }
        finally
        {
            owner.Close();
        }
    }
}
