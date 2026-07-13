using System.Runtime.InteropServices;

namespace IGoLibrary.Ex.Desktop.Services;

public interface IAlertSoundService
{
    Task PlayAsync(CancellationToken cancellationToken = default);

    Task PlaySystemPromptAsync(CancellationToken cancellationToken = default);
}

public sealed class AlertSoundService : IAlertSoundService
{
    private const uint WindowsDefaultSound = uint.MaxValue;
    private const uint WindowsExclamationSound = 0x00000030;

    public Task PlayAsync(CancellationToken cancellationToken = default)
    {
        return PlayCoreAsync(WindowsDefaultSound, cancellationToken);
    }

    public Task PlaySystemPromptAsync(CancellationToken cancellationToken = default)
    {
        return PlayCoreAsync(WindowsExclamationSound, cancellationToken);
    }

    private static Task PlayCoreAsync(uint windowsSoundType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (OperatingSystem.IsWindows())
        {
            MessageBeep(windowsSoundType);
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
