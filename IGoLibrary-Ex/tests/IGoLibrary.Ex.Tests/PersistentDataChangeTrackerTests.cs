using IGoLibrary.Ex.Infrastructure.DataTransfer;
using IGoLibrary.Ex.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace IGoLibrary.Ex.Tests;

public sealed class PersistentDataChangeTrackerTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void PauseState_IsDurable_AndSuccessfulSynchronizationClearsIt()
    {
        Directory.CreateDirectory(_directory);
        var locations = new StorageLocations(_directory, Path.Combine(_directory, "logs"));
        var tracker = new PersistentDataChangeTracker(
            locations,
            NullLogger<PersistentDataChangeTracker>.Instance);

        tracker.MarkChanged(true, "本地导入需要确认");

        var restored = new PersistentDataChangeTracker(
            locations,
            NullLogger<PersistentDataChangeTracker>.Instance);
        Assert.True(restored.IsDirty);
        Assert.True(restored.IsAutomaticUploadPaused);
        Assert.Equal("本地导入需要确认", restored.AutomaticUploadPauseReason);

        restored.MarkSynchronized(restored.Version);
        Assert.False(restored.IsDirty);
        Assert.False(restored.IsAutomaticUploadPaused);
        Assert.Null(restored.AutomaticUploadPauseReason);
    }

    [Fact]
    public void MarkChanged_IsolatesFailingSubscribers()
    {
        Directory.CreateDirectory(_directory);
        var locations = new StorageLocations(_directory, Path.Combine(_directory, "logs"));
        var tracker = new PersistentDataChangeTracker(
            locations,
            NullLogger<PersistentDataChangeTracker>.Instance);
        var successfulSubscriberCalls = 0;
        tracker.Changed += (_, _) => throw new InvalidOperationException("订阅者失败");
        tracker.Changed += (_, _) => successfulSubscriberCalls++;

        tracker.MarkChanged();

        Assert.True(tracker.IsDirty);
        Assert.Equal(1, successfulSubscriberCalls);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
