using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class SeatLabelService(
    ISeatLabelRepository repository,
    IActivityLogService activityLogService) : ISeatLabelService
{
    public const int MaxLabelLength = 32;

    public Task<IReadOnlyList<SeatLabel>> GetLabelsAsync(
        int libraryId,
        CancellationToken cancellationToken = default)
    {
        return repository.GetLabelsAsync(libraryId, cancellationToken);
    }

    public async Task<IReadOnlyList<SeatLabel>> SetLabelsAsync(
        int libraryId,
        IReadOnlyList<SeatReference> seats,
        string text,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seats);
        var normalizedText = NormalizeLabelText(text);
        var labels = seats
            .Where(static seat => !string.IsNullOrWhiteSpace(seat.SeatKey))
            .DistinctBy(static seat => seat.SeatKey, StringComparer.Ordinal)
            .Select(seat => new SeatLabel(seat.SeatKey, seat.SeatName, normalizedText))
            .ToArray();

        if (labels.Length == 0)
        {
            return labels;
        }

        await repository.SetLabelsAsync(libraryId, labels, cancellationToken);
        activityLogService.Write(
            LogEntryKind.Success,
            "SeatLabel",
            $"已为场馆 {libraryId} 的 {labels.Length} 个座位保存标签。");
        return labels;
    }

    public async Task DeleteLabelsAsync(
        int libraryId,
        IReadOnlyList<string> seatKeys,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(seatKeys);
        var normalizedKeys = seatKeys
            .Where(static seatKey => !string.IsNullOrWhiteSpace(seatKey))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (normalizedKeys.Length == 0)
        {
            return;
        }

        await repository.DeleteLabelsAsync(libraryId, normalizedKeys, cancellationToken);
        activityLogService.Write(
            LogEntryKind.Success,
            "SeatLabel",
            $"已删除场馆 {libraryId} 的 {normalizedKeys.Length} 个座位标签。");
    }

    public static string NormalizeLabelText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Any(char.IsControl))
        {
            throw new ArgumentException("标签不能包含换行或控制字符。", nameof(text));
        }

        var normalized = text.Trim();
        if (normalized.Length == 0)
        {
            throw new ArgumentException("标签不能为空或仅包含空白字符。", nameof(text));
        }

        if (normalized.Length > MaxLabelLength)
        {
            throw new ArgumentException($"标签不能超过 {MaxLabelLength} 个字符。", nameof(text));
        }

        return normalized;
    }
}
