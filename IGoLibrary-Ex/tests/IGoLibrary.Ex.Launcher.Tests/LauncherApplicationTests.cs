using System.Diagnostics;
using IGoLibrary.Ex.Launcher;

namespace IGoLibrary.Ex.Launcher.Tests;

public sealed class LauncherApplicationTests
{
    [Fact]
    public void Run_StartsFixedEntryFromAppDirectory_AndPreservesArgumentBoundaries()
    {
        var launcherPath = @"C:\用户 目录\我去图书馆\IGoLibrary-Ex.exe";
        var appDirectory = @"C:\用户 目录\我去图书馆\app";
        var entryExecutable = Path.Combine(appDirectory, "IGoLibrary.Ex.Desktop.exe");
        var platform = new FakeLauncherPlatform(launcherPath)
        {
            ExistingDirectory = appDirectory,
            ExistingFile = entryExecutable,
            StartResult = true
        };
        string[] arguments = ["普通参数", "value with spaces", "", "\"quoted\"", @"尾部反斜杠\"];

        var exitCode = LauncherApplication.Run(arguments, platform);

        Assert.Equal(LauncherApplication.SuccessExitCode, exitCode);
        var startInfo = Assert.IsType<ProcessStartInfo>(platform.StartInfo);
        Assert.Equal(entryExecutable, startInfo.FileName);
        Assert.Equal(appDirectory, startInfo.WorkingDirectory);
        Assert.False(startInfo.UseShellExecute);
        Assert.Equal(arguments, startInfo.ArgumentList);
        Assert.Empty(platform.Errors);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("IGoLibrary-Ex.exe")]
    public void Run_RejectsMissingOrRelativeLauncherPath(string? launcherPath)
    {
        var platform = new FakeLauncherPlatform(launcherPath);

        var exitCode = LauncherApplication.Run([], platform);

        Assert.Equal(LauncherApplication.DeploymentFailureExitCode, exitCode);
        Assert.Null(platform.StartInfo);
        Assert.Single(platform.Errors);
        Assert.Contains("完整解压", platform.Errors[0]);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Run_RejectsIncompleteAppDirectory(bool directoryExists, bool fileExists)
    {
        var launcherPath = @"C:\IGoLibrary-Ex\IGoLibrary-Ex.exe";
        var appDirectory = @"C:\IGoLibrary-Ex\app";
        var platform = new FakeLauncherPlatform(launcherPath)
        {
            ExistingDirectory = directoryExists ? appDirectory : null,
            ExistingFile = fileExists
                ? Path.Combine(appDirectory, "IGoLibrary.Ex.Desktop.exe")
                : null
        };

        var exitCode = LauncherApplication.Run([], platform);

        Assert.Equal(LauncherApplication.DeploymentFailureExitCode, exitCode);
        Assert.Null(platform.StartInfo);
        var error = Assert.Single(platform.Errors);
        Assert.Contains("IGoLibrary.Ex.Desktop.exe", error);
        Assert.Contains("安全软件", error);
    }

    [Fact]
    public void Run_ReturnsStartFailure_WhenProcessCreationReturnsFalse()
    {
        var platform = CreateCompletePlatform();

        var exitCode = LauncherApplication.Run([], platform);

        Assert.Equal(LauncherApplication.StartFailureExitCode, exitCode);
        Assert.Contains("未能创建", Assert.Single(platform.Errors));
    }

    [Fact]
    public void Run_ReturnsStartFailure_AndBoundsExceptionText()
    {
        var platform = CreateCompletePlatform();
        platform.StartException = new InvalidOperationException(new string('错', 800));

        var exitCode = LauncherApplication.Run([], platform);

        Assert.Equal(LauncherApplication.StartFailureExitCode, exitCode);
        var error = Assert.Single(platform.Errors);
        Assert.Contains("无法启动", error);
        Assert.Contains("…", error);
        Assert.True(error.Length < 600);
    }

    [Fact]
    public void Run_PreservesDocumentedExitCode_WhenErrorDialogFails()
    {
        var platform = new FakeLauncherPlatform(null)
        {
            ErrorException = new InvalidOperationException("UI unavailable")
        };

        var exitCode = LauncherApplication.Run([], platform);

        Assert.Equal(LauncherApplication.DeploymentFailureExitCode, exitCode);
    }

    private static FakeLauncherPlatform CreateCompletePlatform()
    {
        var appDirectory = @"C:\IGoLibrary-Ex\app";
        return new FakeLauncherPlatform(@"C:\IGoLibrary-Ex\IGoLibrary-Ex.exe")
        {
            ExistingDirectory = appDirectory,
            ExistingFile = Path.Combine(appDirectory, "IGoLibrary.Ex.Desktop.exe")
        };
    }

    private sealed class FakeLauncherPlatform(string? processPath) : ILauncherPlatform
    {
        public string? ProcessPath { get; } = processPath;

        public string? ExistingDirectory { get; init; }

        public string? ExistingFile { get; init; }

        public bool StartResult { get; init; }

        public Exception? StartException { get; set; }

        public Exception? ErrorException { get; init; }

        public ProcessStartInfo? StartInfo { get; private set; }

        public List<string> Errors { get; } = [];

        public bool DirectoryExists(string path) =>
            string.Equals(path, ExistingDirectory, StringComparison.Ordinal);

        public bool FileExists(string path) =>
            string.Equals(path, ExistingFile, StringComparison.Ordinal);

        public bool TryStart(ProcessStartInfo startInfo)
        {
            StartInfo = startInfo;
            if (StartException is not null)
            {
                throw StartException;
            }

            return StartResult;
        }

        public void ShowError(string message)
        {
            Errors.Add(message);
            if (ErrorException is not null)
            {
                throw ErrorException;
            }
        }
    }
}
