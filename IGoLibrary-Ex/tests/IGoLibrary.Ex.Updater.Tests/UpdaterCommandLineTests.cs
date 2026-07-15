namespace IGoLibrary.Ex.Updater.Tests;

public sealed class UpdaterCommandLineTests
{
    private readonly string _baseDirectory = Path.Combine(
        Path.GetTempPath(),
        "我去图书馆 Updater Tests");

    [Fact]
    public void Parse_BootstrapHasHighestPriority()
    {
        var result = UpdaterCommandLine.Parse(
            ["--cleanup", "--bootstrap", "--pipe", "pipe-name"],
            _baseDirectory);

        Assert.True(result.Succeeded);
        Assert.Equal(UpdaterMode.Bootstrap, result.Command!.Mode);
        Assert.Equal("pipe-name", result.Command.PipeName);
        Assert.Null(result.Command.RequestPath);
    }

    [Fact]
    public void Parse_BootstrapRejectsMissingPipe()
    {
        var result = UpdaterCommandLine.Parse(["--bootstrap"], _baseDirectory);

        Assert.False(result.Succeeded);
        Assert.Equal(UpdaterMode.Bootstrap, result.Mode);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Parse_CleanupCollectsOnlyPositiveProcessIds()
    {
        var requestPath = Path.Combine(_baseDirectory, "事务 1", "request.json");

        var result = UpdaterCommandLine.Parse(
            [
                "--cleanup",
                "--request",
                requestPath,
                "--wait-pid",
                "42",
                "--wait-pid",
                "invalid",
                "--wait-pid",
                "7"
            ],
            _baseDirectory);

        Assert.True(result.Succeeded);
        Assert.Equal(UpdaterMode.Cleanup, result.Command!.Mode);
        Assert.Equal(Path.GetFullPath(requestPath), result.Command.RequestPath);
        Assert.Equal([42, 7], result.Command.WaitProcessIds);
    }

    [Fact]
    public void Parse_RecoveryCoordinatorDefaultsToLocalRequest()
    {
        var result = UpdaterCommandLine.Parse(["--recover"], _baseDirectory);

        Assert.True(result.Succeeded);
        Assert.Equal(UpdaterMode.RecoveryCoordinator, result.Command!.Mode);
        Assert.Equal(
            Path.GetFullPath(Path.Combine(_baseDirectory, "request.json")),
            result.Command.RequestPath);
    }

    [Fact]
    public void Parse_RecoveryWorkerRejectsInvalidCoordinatorPid()
    {
        var result = UpdaterCommandLine.Parse(
            ["--recover-worker", "--request", "request.json", "--recovery-coordinator-pid", "0"],
            _baseDirectory);

        Assert.False(result.Succeeded);
        Assert.Equal(UpdaterMode.RecoveryWorker, result.Mode);
        Assert.NotNull(result.ErrorMessage);
    }

    [Fact]
    public void Parse_RecoveryWorkerAcceptsPositiveCoordinatorPid()
    {
        var requestPath = Path.Combine(_baseDirectory, "恢复 事务", "request.json");

        var result = UpdaterCommandLine.Parse(
            [
                "--recover-worker",
                "--request",
                requestPath,
                "--recovery-coordinator-pid",
                "1234"
            ],
            _baseDirectory);

        Assert.True(result.Succeeded);
        Assert.Equal(UpdaterMode.RecoveryWorker, result.Command!.Mode);
        Assert.Equal(1234, result.Command.RecoveryCoordinatorProcessId);
        Assert.Equal(Path.GetFullPath(requestPath), result.Command.RequestPath);
    }

    [Fact]
    public void Parse_WorkerAcceptsUnicodeRequestPath()
    {
        var requestPath = Path.Combine(_baseDirectory, "worker 中文", "request.json");

        var result = UpdaterCommandLine.Parse(
            ["--worker", "--request", requestPath],
            _baseDirectory);

        Assert.True(result.Succeeded);
        Assert.Equal(UpdaterMode.Worker, result.Command!.Mode);
        Assert.Equal(Path.GetFullPath(requestPath), result.Command.RequestPath);
        Assert.False(result.Command.ExternalWorker);
    }

    [Fact]
    public void Parse_CoordinatorPreservesExternalWorkerFlagAndUnicodePath()
    {
        var requestPath = Path.Combine(_baseDirectory, "中文 路径", "request.json");

        var result = UpdaterCommandLine.Parse(
            ["--request", requestPath, "--external-worker"],
            _baseDirectory);

        Assert.True(result.Succeeded);
        Assert.Equal(UpdaterMode.Coordinator, result.Command!.Mode);
        Assert.True(result.Command.ExternalWorker);
        Assert.Equal(Path.GetFullPath(requestPath), result.Command.RequestPath);
    }

    [Fact]
    public void Parse_MissingWorkerRequestRemainsHeadlessValidationFailure()
    {
        var result = UpdaterCommandLine.Parse(["--worker"], _baseDirectory);

        Assert.False(result.Succeeded);
        Assert.Equal(UpdaterMode.Worker, result.Mode);
        Assert.NotNull(result.ErrorMessage);
    }
}
