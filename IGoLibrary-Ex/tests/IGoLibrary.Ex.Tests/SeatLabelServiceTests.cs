using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Tests;

public sealed class SeatLabelServiceTests
{
    [Fact]
    public async Task SetLabelsAsync_NormalizesTextDeduplicatesSeatsAndDoesNotLogText()
    {
        var repository = new RecordingSeatLabelRepository();
        var log = new ActivityLogService();
        var service = new SeatLabelService(repository, log);

        var result = await service.SetLabelsAsync(
            7,
            [new SeatReference("seat-1", "1"), new SeatReference("seat-1", "旧名称"), new SeatReference("seat-2", "2")],
            "  靠窗位置  ");

        Assert.Equal(2, result.Count);
        Assert.All(result, label => Assert.Equal("靠窗位置", label.Text));
        Assert.Equal(result, repository.SavedLabels);
        var entry = Assert.Single(log.Entries);
        Assert.Equal("SeatLabel", entry.Category);
        Assert.DoesNotContain("靠窗位置", entry.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("第一行\n第二行")]
    [InlineData("标签\t内容")]
    public async Task SetLabelsAsync_RejectsInvalidText(string text)
    {
        var repository = new RecordingSeatLabelRepository();
        var service = new SeatLabelService(repository, new ActivityLogService());

        await Assert.ThrowsAsync<ArgumentException>(() => service.SetLabelsAsync(
            7,
            [new SeatReference("seat-1", "1")],
            text));

        Assert.Empty(repository.SavedLabels);
    }

    [Fact]
    public async Task SetLabelsAsync_RejectsTextLongerThanThirtyTwoCharacters()
    {
        var service = new SeatLabelService(new RecordingSeatLabelRepository(), new ActivityLogService());

        var exception = await Assert.ThrowsAsync<ArgumentException>(() => service.SetLabelsAsync(
            7,
            [new SeatReference("seat-1", "1")],
            new string('座', 33)));

        Assert.Contains("32", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptyTargetSetsAndDeletes_AreNoOps()
    {
        var repository = new RecordingSeatLabelRepository();
        var log = new ActivityLogService();
        var service = new SeatLabelService(repository, log);

        var saved = await service.SetLabelsAsync(7, [], "常用");
        await service.DeleteLabelsAsync(7, []);

        Assert.Empty(saved);
        Assert.Equal(0, repository.SetCalls);
        Assert.Equal(0, repository.DeleteCalls);
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task DeleteLabelsAsync_DeduplicatesKeysAndDoesNotLogLabelContent()
    {
        var repository = new RecordingSeatLabelRepository();
        var log = new ActivityLogService();
        var service = new SeatLabelService(repository, log);

        await service.DeleteLabelsAsync(9, ["seat-1", "seat-1", "seat-2", ""]);

        Assert.Equal(["seat-1", "seat-2"], repository.DeletedKeys);
        Assert.DoesNotContain(log.Entries, entry => entry.Message.Contains("常用", StringComparison.Ordinal));
    }

    private sealed class RecordingSeatLabelRepository : ISeatLabelRepository
    {
        public IReadOnlyList<SeatLabel> SavedLabels { get; private set; } = [];

        public IReadOnlyList<string> DeletedKeys { get; private set; } = [];

        public int SetCalls { get; private set; }

        public int DeleteCalls { get; private set; }

        public Task<IReadOnlyList<SeatLabel>> GetLabelsAsync(int libraryId, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<SeatLabel>>([]);

        public Task SetLabelsAsync(
            int libraryId,
            IReadOnlyList<SeatLabel> labels,
            CancellationToken cancellationToken = default)
        {
            SetCalls++;
            SavedLabels = labels.ToArray();
            return Task.CompletedTask;
        }

        public Task DeleteLabelsAsync(
            int libraryId,
            IReadOnlyList<string> seatKeys,
            CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            DeletedKeys = seatKeys.ToArray();
            return Task.CompletedTask;
        }
    }
}
