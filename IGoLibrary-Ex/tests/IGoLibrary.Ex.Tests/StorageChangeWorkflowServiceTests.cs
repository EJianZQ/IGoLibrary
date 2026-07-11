using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class StorageChangeWorkflowServiceTests
{
    [Fact]
    public async Task ApplyAsync_WhenMigrationIsCanceled_DoesNotStageOrRestart()
    {
        var context = new Context();
        context.Dialog.MigrationDecision = StorageMigrationDecision.Cancel;

        var result = await context.Service.ApplyAsync(context.Target);

        Assert.False(result);
        Assert.Null(context.Storage.StagedChange);
        Assert.Equal(0, context.Restart.RestartCalls);
    }

    [Fact]
    public async Task ApplyAsync_MigratesOverExistingDatabase_StopsActiveTasksAndRestarts()
    {
        var context = new Context();
        context.Storage.TargetDatabaseInspection = new StorageTargetDatabaseInspection(true, true, null);
        context.Grab.EmitStatus(new CoordinatorStatus(
            CoordinatorTaskState.Running,
            "抢座",
            "运行中",
            DateTimeOffset.Now,
            DateTimeOffset.Now));

        var result = await context.Service.ApplyAsync(context.Target);

        Assert.True(result);
        Assert.Equal(1, context.Dialog.OverwritePrompts);
        Assert.Equal(["抢座"], context.Dialog.LastStopTaskNames);
        Assert.Equal(1, context.Grab.StopCalls);
        Assert.NotNull(context.Storage.StagedChange);
        Assert.True(context.Storage.StagedChange.MigrateData);
        Assert.True(context.Storage.StagedChange.MigrateLogs);
        Assert.True(context.Storage.StagedChange.OverwriteTargetDatabase);
        Assert.Equal(1, context.Restart.RestartCalls);
    }

    [Fact]
    public async Task ApplyAsync_WhenTaskStopIsRejected_DoesNotStageChange()
    {
        var context = new Context();
        context.Dialog.ConfirmStopTasksResult = false;
        context.Grab.EmitStatus(new CoordinatorStatus(
            CoordinatorTaskState.Running,
            "抢座",
            "运行中",
            DateTimeOffset.Now,
            DateTimeOffset.Now));

        var result = await context.Service.ApplyAsync(context.Target);

        Assert.False(result);
        Assert.Equal(0, context.Grab.StopCalls);
        Assert.Null(context.Storage.StagedChange);
    }

    [Fact]
    public async Task ApplyAsync_WhenRestartFails_CancelsPendingChange()
    {
        var context = new Context();
        context.Restart.Exception = new InvalidOperationException("restart failed");

        await Assert.ThrowsAsync<InvalidOperationException>(() => context.Service.ApplyAsync(context.Target));

        Assert.Null(context.Storage.StagedChange);
    }

    [Fact]
    public async Task ApplyAsync_WithoutMigrationToValidDatabase_RequiresUseExistingConfirmation()
    {
        var context = new Context();
        context.Dialog.MigrationDecision = StorageMigrationDecision.DoNotMigrate;
        context.Storage.TargetDatabaseInspection = new StorageTargetDatabaseInspection(true, true, null);

        var result = await context.Service.ApplyAsync(context.Target);

        Assert.True(result);
        Assert.Equal(1, context.Dialog.UseExistingPrompts);
        Assert.NotNull(context.Storage.StagedChange);
        Assert.False(context.Storage.StagedChange.MigrateData);
        Assert.False(context.Storage.StagedChange.OverwriteTargetDatabase);
        Assert.Equal(1, context.Restart.RestartCalls);
    }

    [Fact]
    public async Task ApplyAsync_WhenUseExistingDatabaseIsRejected_DoesNotStageOrRestart()
    {
        var context = new Context();
        context.Dialog.MigrationDecision = StorageMigrationDecision.DoNotMigrate;
        context.Dialog.ConfirmUseExistingResult = false;
        context.Storage.TargetDatabaseInspection = new StorageTargetDatabaseInspection(true, true, null);

        var result = await context.Service.ApplyAsync(context.Target);

        Assert.False(result);
        Assert.Equal(1, context.Dialog.UseExistingPrompts);
        Assert.Null(context.Storage.StagedChange);
        Assert.Equal(0, context.Restart.RestartCalls);
    }

    [Fact]
    public async Task ApplyAsync_WithoutMigrationToInvalidDatabase_RejectsBeforeStoppingTasks()
    {
        var context = new Context();
        context.Dialog.MigrationDecision = StorageMigrationDecision.DoNotMigrate;
        context.Storage.TargetDatabaseInspection = new StorageTargetDatabaseInspection(
            true,
            false,
            "not a database");
        context.Grab.EmitStatus(new CoordinatorStatus(
            CoordinatorTaskState.Running,
            "抢座",
            "运行中",
            DateTimeOffset.Now,
            DateTimeOffset.Now));

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() =>
            context.Service.ApplyAsync(context.Target));

        Assert.Contains("not a database", exception.Message);
        Assert.Equal(0, context.Grab.StopCalls);
        Assert.Null(context.Storage.StagedChange);
        Assert.Equal(0, context.Restart.RestartCalls);
    }

    private sealed class Context
    {
        public Context()
        {
            var root = Path.Combine(Path.GetTempPath(), "IGoLibrary-Ex-WorkflowTests");
            Storage.Current = new StorageLocations(Path.Combine(root, "old-data"), Path.Combine(root, "old-logs"));
            Storage.Defaults = Storage.Current;
            Target = new StorageLocations(Path.Combine(root, "new-data"), Path.Combine(root, "new-logs"));
            Service = new StorageChangeWorkflowService(
                Storage,
                Dialog,
                Restart,
                Grab,
                Global,
                Occupy,
                Tomorrow,
                new ActivityLogService());
        }

        public FakeStorageLocationService Storage { get; } = new();

        public FakeStorageChangeDialogService Dialog { get; } = new();

        public FakeApplicationRestartService Restart { get; } = new();

        public FakeGrabSeatCoordinator Grab { get; } = new();

        public FakeGlobalLeakCoordinator Global { get; } = new();

        public FakeOccupySeatCoordinator Occupy { get; } = new();

        public FakeTomorrowReservationCoordinator Tomorrow { get; } = new();

        public StorageLocations Target { get; }

        public StorageChangeWorkflowService Service { get; }
    }
}
