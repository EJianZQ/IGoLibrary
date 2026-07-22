using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Desktop;

internal sealed class CloudflaredDownloadConfirmationWindow : Window
{
    public CloudflaredDownloadConfirmationWindow(
        CloudflaredAssetDescriptor asset,
        string installDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installDirectory);
        Title = "下载 Cloudflare Tunnel 组件";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        LaterButton = CreateButton("暂不");
        LaterButton.Click += (_, _) => Close(false);
        DownloadButton = CreateButton("下载并安装");
        DownloadButton.Click += (_, _) => Close(true);
        MessageText =
            $"当前未找到有效的 cloudflared。是否从 Cloudflare 官方 GitHub Release 下载并安装？\n\n" +
            $"版本：{asset.Version}\n" +
            $"平台：{asset.RuntimeIdentifier}\n" +
            $"下载大小：{FormatBytes(asset.DownloadSize)}\n\n" +
            $"组件会安装：{installDirectory}";

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
                        Text = "需要下载 cloudflared",
                        FontSize = 21,
                        FontWeight = FontWeight.Bold
                    },
                    new TextBlock
                    {
                        Text = MessageText,
                        TextWrapping = TextWrapping.Wrap
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Right,
                        Spacing = 10,
                        Children = { LaterButton, DownloadButton }
                    }
                }
            }
        };
    }

    internal Button LaterButton { get; }

    internal Button DownloadButton { get; }

    internal string MessageText { get; }

    private static Button CreateButton(string text)
        => new()
        {
            Content = text,
            MinWidth = 112,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };

    private static string FormatBytes(long value)
        => $"{(double)value / (1024 * 1024):F1} MiB";
}
