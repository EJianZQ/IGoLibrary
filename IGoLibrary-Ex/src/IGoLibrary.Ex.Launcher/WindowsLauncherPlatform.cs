using System.Diagnostics;
using System.Runtime.InteropServices;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

namespace IGoLibrary.Ex.Launcher;

internal sealed class WindowsLauncherPlatform : ILauncherPlatform
{
    private const string ErrorCaption = "我去图书馆 - 启动失败";
    private const uint MessageBoxOk = 0x00000000;
    private const uint MessageBoxIconError = 0x00000010;
    private const uint MessageBoxSetForeground = 0x00010000;

    public static WindowsLauncherPlatform Instance { get; } = new();

    private WindowsLauncherPlatform()
    {
    }

    public string? ProcessPath => Environment.ProcessPath;

    public bool DirectoryExists(string path) => Directory.Exists(path);

    public bool FileExists(string path) => File.Exists(path);

    public bool TryStart(ProcessStartInfo startInfo)
    {
        using var process = Process.Start(startInfo);
        return process is not null;
    }

    public void ShowError(string message)
    {
        _ = NativeMethods.MessageBox(
            0,
            message,
            ErrorCaption,
            MessageBoxOk | MessageBoxIconError | MessageBoxSetForeground);
    }
}

internal static partial class NativeMethods
{
    [LibraryImport(
        "user32.dll",
        EntryPoint = "MessageBoxW",
        StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int MessageBox(
        nint parentWindow,
        string text,
        string caption,
        uint type);
}
