using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IGlobalLeakSeatBlacklistService
{
    Task<IReadOnlyDictionary<int, IReadOnlyList<SeatReference>>> LoadAsync(
        IReadOnlyList<int> libraryIds, CancellationToken cancellationToken = default);

    /// <summary>原子替换提交场馆的黑名单；空列表清空该场馆，未提交场馆保持不变。</summary>
    Task SaveAsync(IReadOnlyDictionary<int, IReadOnlyList<SeatReference>> changes,
        CancellationToken cancellationToken = default);
}
