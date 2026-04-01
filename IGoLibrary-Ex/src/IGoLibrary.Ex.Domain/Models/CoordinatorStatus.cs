using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record CoordinatorStatus(
    CoordinatorTaskState State,
    string Title,
    string Message,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastUpdatedAt,
    int PollCount = 0,
    int RequestCount = 0,
    DateTimeOffset? LastRequestAt = null)
{
    public static CoordinatorStatus Idle(string title) => new(
        CoordinatorTaskState.Idle,
        title,
        "未运行",
        null,
        null,
        0,
        0,
        null);
}
