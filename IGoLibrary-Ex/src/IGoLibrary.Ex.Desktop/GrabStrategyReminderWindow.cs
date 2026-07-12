using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Desktop;

internal sealed class GrabStrategyReminderWindow : Window
{
    private const string ReminderMessage =
        "选择座位过多，当前抢座策略效率可能不如先获取列表判断状态，是否需要切换至最优策略";

    public GrabStrategyReminderWindow()
    {
        Title = "抢座策略提醒";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        MinWidth = 460;
        MaxHeight = 480;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        DisableReminderCheckBox = new CheckBox
        {
            Content = "不再提醒"
        };

        KeepCurrentButton = CreateButton("保持当前策略并开始", 160);
        KeepCurrentButton.Click += (_, _) => Close(BuildResult(GrabStrategyReminderDecision.KeepCurrent));

        SwitchToOptimalButton = CreateButton("切换并开始", 132);
        SwitchToOptimalButton.Click += (_, _) => Close(BuildResult(GrabStrategyReminderDecision.SwitchToOptimal));

        Content = new Border
        {
            Padding = new Thickness(24),
            Child = new StackPanel
            {
                Spacing = 18,
                Children =
                {
                    new TextBlock
                    {
                        Text = Title,
                        FontSize = 22,
                        FontWeight = FontWeight.Bold
                    },
                    new TextBlock
                    {
                        Text = ReminderMessage,
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap
                    },
                    DisableReminderCheckBox,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { KeepCurrentButton, SwitchToOptimalButton }
                    }
                }
            }
        };

        KeyDown += OnKeyDown;
    }

    internal CheckBox DisableReminderCheckBox { get; }

    internal Button KeepCurrentButton { get; }

    internal Button SwitchToOptimalButton { get; }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        Close(GrabStrategyReminderResult.Cancelled);
    }

    private GrabStrategyReminderResult BuildResult(GrabStrategyReminderDecision decision)
    {
        return new GrabStrategyReminderResult(
            decision,
            DisableReminderCheckBox.IsChecked == true);
    }

    private static Button CreateButton(string text, double minWidth)
        => new()
        {
            Content = text,
            MinWidth = minWidth,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
}
