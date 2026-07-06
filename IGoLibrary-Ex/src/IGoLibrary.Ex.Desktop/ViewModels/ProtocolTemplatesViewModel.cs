using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class ProtocolTemplatesViewModel(
    IProtocolTemplateEditorService protocolTemplateEditorService,
    IActivityLogService activityLogService,
    INotificationService notificationService) : ViewModelBase
{
    private DeferredAutoSaveController? _autoSave;

    private bool _isLoading;
    private Func<bool> _canAutoSave = static () => false;
    private Action _graphQlOverridesChanged = static () => { };

    private DeferredAutoSaveController AutoSave => _autoSave ??= new DeferredAutoSaveController(
        TimeSpan.FromMilliseconds(450),
        cancellationToken => protocolTemplateEditorService.SaveOverridesAsync(BuildOverridesSnapshot(), cancellationToken));

    public bool HasPendingAutoSave => _autoSave?.HasPending == true;

    [ObservableProperty]
    private bool traceIntGraphQlOverridesEnabled;

    partial void OnTraceIntGraphQlOverridesEnabledChanged(bool value)
    {
        _graphQlOverridesChanged();
    }

    [ObservableProperty]
    private string getCookieTemplateText = string.Empty;

    [ObservableProperty]
    private string queryLibrariesTemplateText = string.Empty;

    [ObservableProperty]
    private string queryLibraryLayoutTemplateText = string.Empty;

    [ObservableProperty]
    private string queryLibraryRuleTemplateText = string.Empty;

    [ObservableProperty]
    private string queryReservationInfoTemplateText = string.Empty;

    [ObservableProperty]
    private string reserveSeatTemplateText = string.Empty;

    [ObservableProperty]
    private string cancelReservationTemplateText = string.Empty;

    [ObservableProperty]
    private string tomorrowReservationQueueUrlTemplateText = string.Empty;

    [ObservableProperty]
    private string tomorrowReservationWarmUpTemplateText = string.Empty;

    [ObservableProperty]
    private string tomorrowReservationSaveTemplateText = string.Empty;

    [ObservableProperty]
    private string tomorrowReservationInfoTemplateText = string.Empty;

    partial void OnGetCookieTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnQueryLibrariesTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnQueryLibraryLayoutTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnQueryLibraryRuleTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnQueryReservationInfoTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnReserveSeatTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnCancelReservationTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnTomorrowReservationQueueUrlTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnTomorrowReservationWarmUpTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnTomorrowReservationSaveTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnTomorrowReservationInfoTemplateTextChanged(string value) => ScheduleAutoSave();

    public void ConfigureAutoSave(Func<bool> canAutoSave, Action graphQlOverridesChanged)
    {
        _canAutoSave = canAutoSave;
        _graphQlOverridesChanged = graphQlOverridesChanged;
    }

    [RelayCommand]
    public async Task SaveProtocolOverridesAsync()
    {
        AutoSave.Cancel();
        await PersistAsync();
        await notificationService.ShowSuccessAsync("协议模板已保存", "高级协议覆盖已写入数据库");
    }

    [RelayCommand]
    public async Task ResetProtocolOverridesAsync()
    {
        AutoSave.Cancel();
        await protocolTemplateEditorService.ResetOverridesAsync();
        await LoadAsync();
        await notificationService.ShowSuccessAsync("协议模板已重置", "已恢复内置默认模板");
    }

    public Task PersistAsync(CancellationToken cancellationToken = default)
    {
        return protocolTemplateEditorService.SaveOverridesAsync(BuildOverridesSnapshot(), cancellationToken);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _isLoading = true;
        try
        {
            var templates = await protocolTemplateEditorService.LoadTemplatesAsync(cancellationToken);
            GetCookieTemplateText = templates.GetCookieUrlTemplate;
            QueryLibrariesTemplateText = templates.QueryLibrariesTemplate;
            QueryLibraryLayoutTemplateText = templates.QueryLibraryLayoutTemplate;
            QueryLibraryRuleTemplateText = templates.QueryLibraryRuleTemplate;
            QueryReservationInfoTemplateText = templates.QueryReservationInfoTemplate;
            ReserveSeatTemplateText = templates.ReserveSeatTemplate;
            CancelReservationTemplateText = templates.CancelReservationTemplate;
            TomorrowReservationQueueUrlTemplateText = templates.TomorrowReservationQueueUrlTemplate;
            TomorrowReservationWarmUpTemplateText = templates.TomorrowReservationWarmUpTemplate;
            TomorrowReservationSaveTemplateText = templates.TomorrowReservationSaveTemplate;
            TomorrowReservationInfoTemplateText = templates.TomorrowReservationInfoTemplate;
        }
        finally
        {
            _isLoading = false;
        }
    }

    public void CancelPendingAutoSave()
    {
        AutoSave.Cancel();
    }

    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        return AutoSave.FlushAsync(cancellationToken);
    }

    private TraceIntGraphQlTemplateOverrides BuildOverridesSnapshot()
    {
        return new TraceIntGraphQlTemplateOverrides(
            GetCookieTemplateText,
            QueryLibrariesTemplateText,
            QueryLibraryLayoutTemplateText,
            QueryLibraryRuleTemplateText,
            QueryReservationInfoTemplateText,
            ReserveSeatTemplateText,
            CancelReservationTemplateText,
            TomorrowReservationQueueUrlTemplateText,
            TomorrowReservationWarmUpTemplateText,
            TomorrowReservationSaveTemplateText,
            TomorrowReservationInfoTemplateText);
    }

    private void ScheduleAutoSave()
    {
        if (_isLoading || !TraceIntGraphQlOverridesEnabled || !_canAutoSave())
        {
            return;
        }

        AutoSave.Schedule(ex =>
            activityLogService.Write(LogEntryKind.Warning, "Settings", $"自动保存接口模板失败：{ex.Message}"));
    }
}
