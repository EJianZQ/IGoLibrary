using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class AppWindowService
{
    private Window? _mainWindow;

    public bool AllowClose { get; private set; }

    public Window? MainWindow => _mainWindow;

    public void Attach(Window window)
    {
        _mainWindow = window;
    }

    public void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.Activate();
        _mainWindow.WindowState = WindowState.Normal;
    }

    public void HideMainWindow()
    {
        _mainWindow?.Hide();
    }

    public void QuitApplication()
    {
        AllowClose = true;
        if (Avalonia.Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
