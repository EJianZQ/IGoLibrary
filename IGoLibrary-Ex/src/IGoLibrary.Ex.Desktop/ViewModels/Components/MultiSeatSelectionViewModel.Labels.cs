using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class MultiSeatSelectionViewModel
{
    private bool _isSynchronizingSeatLabels;

    [ObservableProperty]
    private bool isSeatLabelOperationInProgress;

    public void ApplySeatLabels(IEnumerable<SeatLabel> labels)
    {
        var labelsBySeatKey = labels
            .DistinctBy(static label => label.SeatKey, StringComparer.Ordinal)
            .ToDictionary(static label => label.SeatKey, StringComparer.Ordinal);

        _isSynchronizingSeatLabels = true;
        try
        {
            foreach (var seat in _allSeats)
            {
                seat.LabelText = labelsBySeatKey.TryGetValue(seat.SeatKey, out var label)
                    ? label.Text
                    : null;
            }
        }
        finally
        {
            _isSynchronizingSeatLabels = false;
        }

        _ = ApplySeatFilterAsync();
    }

    partial void OnIsSeatLabelOperationInProgressChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSetSelectedSeatLabel));
    }

    [RelayCommand]
    private async Task SetSelectedSeatLabelAsync()
    {
        if (!CanSetSelectedSeatLabel)
        {
            return;
        }

        var targets = _allSeats
            .Where(static seat => seat.IsSelected)
            .Select(static seat => new SeatReference(seat.SeatKey, seat.SeatName))
            .ToArray();
        if (targets.Length == 0)
        {
            return;
        }

        var text = await seatLabelDialogService.ShowAsync(new SeatLabelDialogRequest(
            "批量设置座位标签",
            $"将为已选的 {targets.Length} 个座位设置同一标签；已有标签会被覆盖。"));
        if (text is null)
        {
            return;
        }

        await SaveSeatLabelsAsync(targets, text, showBatchSuccess: true);
    }

    private async Task EditSeatLabelAsync(SeatItemViewModel seat)
    {
        if (IsSeatLabelOperationInProgress || !CanEditGrabConfiguration())
        {
            return;
        }

        var text = await seatLabelDialogService.ShowAsync(new SeatLabelDialogRequest(
            seat.HasLabel ? "编辑座位标签" : "添加座位标签",
            $"为座位 {seat.SeatName} 设置自定义标签。",
            seat.LabelText));
        if (text is null || string.Equals(text.Trim(), seat.LabelText, StringComparison.Ordinal))
        {
            return;
        }

        await SaveSeatLabelsAsync(
            [new SeatReference(seat.SeatKey, seat.SeatName)],
            text,
            showBatchSuccess: false);
    }

    private async Task SaveSeatLabelsAsync(
        IReadOnlyList<SeatReference> targets,
        string text,
        bool showBatchSuccess)
    {
        var selectedLibrary = _selectedLibrary?.Invoke();
        if (selectedLibrary is null)
        {
            await notificationService.ShowWarningAsync("未绑定场馆", "请先绑定场馆后再设置座位标签");
            return;
        }

        try
        {
            IsSeatLabelOperationInProgress = true;
            var saved = await venueWorkflowService.SetSeatLabelsAsync(
                selectedLibrary.LibraryId,
                targets,
                text);
            ApplySavedLabels(saved);
            if (showBatchSuccess)
            {
                await notificationService.ShowSuccessAsync("标签已保存", $"已为 {saved.Count} 个座位设置标签");
            }
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "SeatLabel", $"保存座位标签失败：{ex.Message}");
            await notificationService.ShowWarningAsync("保存座位标签失败", ex.Message);
        }
        finally
        {
            IsSeatLabelOperationInProgress = false;
        }
    }

    private async Task DeleteSeatLabelAsync(SeatItemViewModel seat)
    {
        if (IsSeatLabelOperationInProgress || !CanEditGrabConfiguration() || !seat.HasLabel)
        {
            return;
        }

        var selectedLibrary = _selectedLibrary?.Invoke();
        if (selectedLibrary is null)
        {
            await notificationService.ShowWarningAsync("未绑定场馆", "请先绑定场馆后再删除座位标签");
            return;
        }

        try
        {
            IsSeatLabelOperationInProgress = true;
            await venueWorkflowService.DeleteSeatLabelsAsync(
                selectedLibrary.LibraryId,
                [seat.SeatKey]);
            seat.LabelText = null;
        }
        catch (Exception ex)
        {
            activityLogService.Write(LogEntryKind.Error, "SeatLabel", $"删除座位标签失败：{ex.Message}");
            await notificationService.ShowWarningAsync("删除座位标签失败", ex.Message);
        }
        finally
        {
            IsSeatLabelOperationInProgress = false;
        }
    }

    private void ApplySavedLabels(IEnumerable<SeatLabel> labels)
    {
        var savedBySeatKey = labels.ToDictionary(static label => label.SeatKey, StringComparer.Ordinal);
        _isSynchronizingSeatLabels = true;
        try
        {
            foreach (var seat in _allSeats.Where(seat => savedBySeatKey.ContainsKey(seat.SeatKey)))
            {
                seat.LabelText = savedBySeatKey[seat.SeatKey].Text;
            }
        }
        finally
        {
            _isSynchronizingSeatLabels = false;
        }

        _ = ApplySeatFilterAsync();
    }
}
