using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using IGoLibrary.Ex.Application.Backup;

namespace IGoLibrary.Ex.Desktop;

internal sealed class BackupComparisonWindow : Window
{
    internal CheckBox UnderstoodCheckBox { get; }

    internal Button RestoreButton { get; }

    public BackupComparisonWindow(PreparedBackup backup)
    {
        Title = "对比备份与本地数据";
        Width = 820;
        Height = 700;
        MinWidth = 680;
        MinHeight = 540;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var understood = new CheckBox
        {
            Content = "我了解当前本地数据和安全凭据将被备份内容完整覆盖",
            Margin = new Thickness(0, 8, 0, 0)
        };
        var restore = new Button
        {
            Content = "确认覆盖并重启",
            MinWidth = 150,
            IsEnabled = false
        };
        understood.IsCheckedChanged += (_, _) => restore.IsEnabled = understood.IsChecked == true;
        UnderstoodCheckBox = understood;
        RestoreButton = restore;
        restore.Click += (_, _) => Close(true);
        var cancel = new Button { Content = "取消", MinWidth = 92 };
        cancel.Click += (_, _) => Close(false);

        var rows = new StackPanel { Spacing = 8 };
        foreach (var item in backup.Comparison.Items)
        {
            rows.Children.Add(CreateDifferenceRow(item));
        }

        var manifest = backup.Manifest;
        var summary =
            $"创建时间：{manifest.CreatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}\n" +
            $"应用版本：{manifest.AppVersion}    数据库版本：{manifest.DatabaseSchemaVersion}    来源：{manifest.SourcePlatform}\n" +
            $"数据库大小：{FormatBytes(manifest.DatabaseLength)}\n" +
            $"差异汇总：新增 {backup.Comparison.AddedCount}，删除 {backup.Comparison.RemovedCount}，变更 {backup.Comparison.ChangedCount}，未变 {backup.Comparison.UnchangedCount}";
        Content = new Border
        {
            Padding = new Thickness(24),
            Child = new Grid
            {
                RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto,Auto"),
                RowSpacing = 14,
                Children =
                {
                    At(new TextBlock
                    {
                        Text = "恢复前数据对比",
                        FontSize = 24,
                        FontWeight = FontWeight.Bold
                    }, 0),
                    At(new TextBlock { Text = summary, TextWrapping = TextWrapping.Wrap }, 1),
                    At(new ScrollViewer
                    {
                        Content = rows,
                        VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
                    }, 2),
                    At(understood, 3),
                    At(new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { cancel, restore }
                    }, 4)
                }
            }
        };
    }

    private static Control CreateDifferenceRow(BackupDifferenceItem item)
    {
        var color = item.Kind switch
        {
            BackupDifferenceKind.Added => Brushes.SeaGreen,
            BackupDifferenceKind.Removed => Brushes.IndianRed,
            BackupDifferenceKind.Changed => Brushes.DarkOrange,
            _ => Brushes.Gray
        };
        var kind = item.Kind switch
        {
            BackupDifferenceKind.Added => "新增",
            BackupDifferenceKind.Removed => "删除",
            BackupDifferenceKind.Changed => "变更",
            _ => "未变"
        };
        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("150,90,*"),
            ColumnSpacing = 10,
            Children =
            {
                AtColumn(new TextBlock { Text = item.Category, FontWeight = FontWeight.SemiBold }, 0),
                AtColumn(new TextBlock { Text = kind, Foreground = color }, 1),
                AtColumn(new TextBlock
                {
                    Text = $"新增 {item.AddedCount} / 删除 {item.RemovedCount} / 变更 {item.ChangedCount} / 未变 {item.UnchangedCount}",
                    TextWrapping = TextWrapping.Wrap
                }, 2)
            }
        };
        var details = new StackPanel { Spacing = 7, Margin = new Thickness(4, 8, 0, 2) };
        foreach (var detail in item.Details ?? [])
        {
            var detailKind = detail.Kind switch
            {
                BackupDifferenceKind.Added => "新增",
                BackupDifferenceKind.Removed => "删除",
                BackupDifferenceKind.Changed => "变更",
                _ => "未变"
            };
            details.Children.Add(new TextBlock
            {
                Text = $"[{detailKind}] {detail.Key}    本地：{detail.LocalValue}    备份：{detail.BackupValue}",
                TextWrapping = TextWrapping.Wrap
            });
        }

        if (details.Children.Count == 0)
        {
            details.Children.Add(new TextBlock
            {
                Text = $"本地：{item.LocalSummary}    备份：{item.BackupSummary}",
                TextWrapping = TextWrapping.Wrap
            });
        }

        return new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromArgb(55, 128, 128, 128)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 9),
            Child = new Expander
            {
                Header = header,
                Content = details
            }
        };
    }

    private static T At<T>(T control, int row) where T : Control
    {
        Grid.SetRow(control, row);
        return control;
    }

    private static T AtColumn<T>(T control, int column) where T : Control
    {
        Grid.SetColumn(control, column);
        return control;
    }

    private static string FormatBytes(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:F2} MiB"
            : bytes >= 1024 ? $"{bytes / 1024d:F1} KiB" : $"{bytes} B";
}
