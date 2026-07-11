namespace IGoLibrary.Ex.Desktop.Startup;

internal sealed record StartupNotice(string Title, string Message)
{
    public static StartupNotice DuplicateInstance { get; } = new(
        "提示",
        "已有一个正在运行的程序，请不要多开");

    public static StartupNotice CreateStartupFailure(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return new StartupNotice(
            "启动失败",
            $"无法确认程序是否可以安全启动，请稍后重试。{Environment.NewLine}{Environment.NewLine}{exception.Message}");
    }
}
