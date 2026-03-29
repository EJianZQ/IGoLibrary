using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Domain.Models;

public sealed record CoordinatorStatus(
    CoordinatorTaskState State,
    string Title,
    string Message,
    DateTimeOffset? StartedAt,
    DateTimeOffset? LastUpdatedAt)
{
    public static CoordinatorStatus Idle(string title) => new(
        CoordinatorTaskState.Idle,
        title,
        "未运行",
        null,
        null);
}
