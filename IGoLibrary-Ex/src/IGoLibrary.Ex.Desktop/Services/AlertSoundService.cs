using System.Runtime.InteropServices;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class AlertSoundService
{
    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (OperatingSystem.IsWindows())
        {
            MessageBeep(uint.MaxValue);
            return Task.CompletedTask;
        }

        try
        {
            Console.Beep();
        }
        catch
        {
            Console.Write("\a");
        }

        return Task.CompletedTask;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MessageBeep(uint type);
}
