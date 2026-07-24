using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Desktop.Services;

public interface IAlertSoundService
{
    Task PlayAsync(CancellationToken cancellationToken = default);

    Task PlaySystemPromptAsync(CancellationToken cancellationToken = default);
}

public sealed class AlertSoundService(ILogger<AlertSoundService>? logger = null) : IAlertSoundService
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

    private Task PlayCoreAsync(uint windowsSoundType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (OperatingSystem.IsWindows())
        {
            if (!MessageBeep(windowsSoundType))
            {
                logger?.LogWarning(
                    "播放 Windows 系统提示音失败。原生错误码={NativeErrorCode}",
                    Marshal.GetLastWin32Error());
            }

            return Task.CompletedTask;
        }

        try
        {
            Console.Beep();
        }
        catch (Exception ex)
        {
            logger?.LogDebug(ex, "当前平台不支持 Console.Beep，改用终端响铃字符。");
            try
            {
                Console.Write("\a");
            }
            catch (Exception fallbackException)
            {
                logger?.LogWarning(fallbackException, "播放终端提示音失败。");
            }
        }

        return Task.CompletedTask;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MessageBeep(uint type);
}
