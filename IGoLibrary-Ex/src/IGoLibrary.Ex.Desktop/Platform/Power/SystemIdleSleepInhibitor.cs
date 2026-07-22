namespace IGoLibrary.Ex.Desktop.Platform.Power;

internal interface ISystemIdleSleepInhibitor : IDisposable
{
    event EventHandler<SystemSleepInhibitorException>? CleanupFailed;

    string PlatformName { get; }

    bool IsSupported { get; }

    bool IsActive { get; }

    void Activate(string reason);

    void Deactivate();
}

internal sealed class SystemSleepInhibitorException(
    string platformName,
    string operation,
    int nativeErrorCode,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public string PlatformName { get; } = platformName;

    public string Operation { get; } = operation;

    public int NativeErrorCode { get; } = nativeErrorCode;
}

internal static class SystemIdleSleepInhibitorFactory
{
    public static ISystemIdleSleepInhibitor Create()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsSystemIdleSleepInhibitor();
        }

        if (OperatingSystem.IsMacOS())
        {
            return new MacSystemIdleSleepInhibitor();
        }

        return new UnsupportedSystemIdleSleepInhibitor();
    }
}

internal sealed class UnsupportedSystemIdleSleepInhibitor : ISystemIdleSleepInhibitor
{
    public event EventHandler<SystemSleepInhibitorException>? CleanupFailed
    {
        add { }
        remove { }
    }

    public string PlatformName => "Unsupported";

    public bool IsSupported => false;

    public bool IsActive => false;

    public void Activate(string reason)
    {
    }

    public void Deactivate()
    {
    }

    public void Dispose()
    {
    }
}
