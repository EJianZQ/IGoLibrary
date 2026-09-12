using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Enums;

namespace IGoLibrary.Ex.Desktop.Services;

/// <summary>两个工作区共享的用户视图偏好；自动降级不调用保存。</summary>
public sealed class SeatViewPreferenceService(ISettingsWorkflowService settings, IActivityLogService activityLog)
{
    private Task? _initialization;
    private Task _pendingSave = Task.CompletedTask;
    private long _selectionVersion;
    private Exception? _saveError;
    public bool PreferList { get; private set; }
    public event EventHandler? Changed;
    public Task InitializeAsync() => _initialization ??= LoadAsync();

    private async Task LoadAsync()
    {
        var version = _selectionVersion;
        try
        {
            var loaded = await settings.LoadAsync();
            if (version != _selectionVersion) return;
            PreferList = loaded.Ui.SeatWorkspaceListView;
            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            activityLog.Write(LogEntryKind.Warning, "Settings", $"读取座位视图偏好失败：{ex.Message}", ex);
        }
    }

    public void Select(bool listView)
    {
        ++_selectionVersion;
        PreferList = listView;
        Changed?.Invoke(this, EventArgs.Empty);
        _pendingSave = SaveAfterAsync(_pendingSave, listView);
    }

    private async Task SaveAfterAsync(Task previous, bool listView)
    {
        await previous;
        try
        {
            await settings.SaveSeatWorkspaceViewAsync(listView);
            _saveError = null;
        }
        catch (Exception ex)
        {
            _saveError = ex;
            activityLog.Write(LogEntryKind.Warning, "Settings", $"保存座位视图偏好失败：{ex.Message}", ex);
        }
    }

    public async Task FlushAsync()
    {
        await _pendingSave;
        if (_saveError is not null) throw new IOException("座位视图偏好尚未保存", _saveError);
    }
}
