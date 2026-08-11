using System.Diagnostics;

namespace IGoLibrary.Ex.Launcher;

internal interface ILauncherPlatform
{
    string? ProcessPath { get; }

    bool DirectoryExists(string path);

    bool FileExists(string path);

    bool TryStart(ProcessStartInfo startInfo);

    void ShowError(string message);
}
