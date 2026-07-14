using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Tests;

public sealed class UpdateProcessStartInfoFactoryTests
{
    [Fact]
    public void CreateWorker_NeverRequestsElevation()
    {
        var executable = Path.GetFullPath("IGoLibrary.Ex.Updater.exe");
        var request = Path.GetFullPath("request file.json");

        var info = UpdateProcessStartInfoFactory.CreateWorker(executable, request);

        Assert.Equal(executable, info.FileName);
        Assert.Equal(["--worker", "--request", request], info.ArgumentList);
        Assert.False(info.UseShellExecute);
        Assert.Equal(string.Empty, info.Verb);
    }

    [Fact]
    public void CreateBootstrap_ExplicitlyRequestsElevation()
    {
        var executable = Path.GetFullPath("IGoLibrary.Ex.Updater.exe");

        var info = UpdateProcessStartInfoFactory.CreateBootstrap(executable, "trusted-pipe-name");

        Assert.Equal(executable, info.FileName);
        Assert.Equal(["--bootstrap", "--pipe", "trusted-pipe-name"], info.ArgumentList);
        Assert.True(info.UseShellExecute);
        Assert.Equal("runas", info.Verb);
    }

    [Fact]
    public void CreateRecoveryWorker_ElevatesOnlyWhenExplicitlyRequested()
    {
        var executable = Path.GetFullPath("IGoLibrary.Ex.Updater.exe");
        var request = Path.GetFullPath("request.json");

        var normal = UpdateProcessStartInfoFactory.CreateRecoveryWorker(
            executable,
            request,
            elevate: false,
            recoveryCoordinatorProcessId: 42);
        var elevated = UpdateProcessStartInfoFactory.CreateRecoveryWorker(
            executable,
            request,
            elevate: true,
            recoveryCoordinatorProcessId: 42);

        Assert.False(normal.UseShellExecute);
        Assert.Equal(string.Empty, normal.Verb);
        Assert.True(elevated.UseShellExecute);
        Assert.Equal("runas", elevated.Verb);
    }

    [Fact]
    public void CreateApplication_AlwaysStartsWithoutShellOrElevation()
    {
        var executable = Path.GetFullPath("IGoLibrary.Ex.Desktop.exe");
        var workingDirectory = Path.GetDirectoryName(executable)!;
        var transactionId = Guid.NewGuid().ToString("N");

        var info = UpdateProcessStartInfoFactory.CreateApplication(
            executable,
            workingDirectory,
            transactionId);

        Assert.False(info.UseShellExecute);
        Assert.Equal(string.Empty, info.Verb);
        Assert.Equal(
            ["--update-transaction", transactionId],
            info.ArgumentList);
    }
}
