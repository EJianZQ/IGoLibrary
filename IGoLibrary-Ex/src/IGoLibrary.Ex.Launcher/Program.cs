namespace IGoLibrary.Ex.Launcher;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        return LauncherApplication.Run(args, WindowsLauncherPlatform.Instance);
    }
}
