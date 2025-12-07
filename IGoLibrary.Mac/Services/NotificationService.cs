using System;
using System.Linq;
using System.Windows.Input;
using IGoLibrary.Core.Interfaces;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia;

namespace IGoLibrary.Mac.Services
{
    public class NotificationService : INotificationService
    {
        public void ShowSuccess(string title, string message)
        {
            ShowMessageBox(title, message, "Success");
        }

        public void ShowError(string title, string message)
        {
            ShowMessageBox(title, message, "Error");
        }

        public void ShowWarning(string title, string message)
        {
            ShowMessageBox(title, message, "Warning");
        }

        private void ShowMessageBox(string title, string message, string type)
        {
            // 确保在UI线程上创建和显示窗口
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
                {
                    Window? messageBoxWindow = null;

                    messageBoxWindow = new Window
                    {
                        Title = title,
                        Width = 400,
                        Height = 200,
                        WindowStartupLocation = WindowStartupLocation.CenterScreen,
                        Content = new StackPanel
                        {
                            Margin = new Thickness(20),
                            Children =
                            {
                                new TextBlock
                                {
                                    Text = $"[{type}] {title}",
                                    FontWeight = Avalonia.Media.FontWeight.Bold,
                                    Margin = new Thickness(0, 0, 0, 10)
                                },
                                new TextBlock
                                {
                                    Text = message,
                                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                                },
                                new Button
                                {
                                    Content = "确定",
                                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                                    Margin = new Thickness(0, 20, 0, 0),
                                    Command = new RelayCommand(() =>
                                    {
                                        messageBoxWindow?.Close();
                                    })
                                }
                            }
                        }
                    };

                    // 使用 Show() 而不是 ShowDialog() - 非模态对话框，不会阻塞UI线程
                    messageBoxWindow.Show();
                }
            });
        }

        private class RelayCommand : ICommand
        {
            private readonly Action _execute;

            public RelayCommand(Action execute)
            {
                _execute = execute;
            }

            public event EventHandler? CanExecuteChanged;

            public bool CanExecute(object? parameter) => true;

            public void Execute(object? parameter) => _execute();
        }
    }
}
