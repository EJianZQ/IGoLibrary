using System.Collections;
using System.ComponentModel;
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
    INotificationService notificationService,
    TimeProvider? timeProvider = null) : ViewModelBase, INotifyDataErrorInfo
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, string[]> _validationErrors = new(StringComparer.Ordinal);
    private TraceIntProtocolValidationResult _validationResult = new([], []);
    private TraceIntProtocolTemplates? _defaultTemplates;
    private DeferredAutoSaveController? _autoSave;
    private bool _isLoading;
    private Func<bool> _canAutoSave = static () => false;
    private Action _graphQlOverridesChanged = static () => { };

    private DeferredAutoSaveController AutoSave => _autoSave ??= new DeferredAutoSaveController(
        TimeSpan.FromMilliseconds(450),
        cancellationToken => protocolTemplateEditorService.SaveOverridesAsync(BuildOverridesSnapshot(), cancellationToken),
        _timeProvider);

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public bool HasPendingAutoSave => _autoSave?.HasPending == true;

    public bool HasErrors => _validationErrors.Count > 0;

    public bool HasProtocolValidationErrors => HasErrors;

    public bool HasProtocolValidationWarnings => _validationResult.Warnings.Count > 0;

    public string ProtocolValidationSummaryText => string.Join(
        Environment.NewLine,
        _validationResult.Errors.Select(static issue => $"• {issue.Message}"));

    public string ProtocolValidationWarningText => string.Join(
        Environment.NewLine,
        _validationResult.Warnings.Select(static issue => $"• {issue.Message}"));

    [ObservableProperty]
    private bool traceIntGraphQlOverridesEnabled;

    partial void OnTraceIntGraphQlOverridesEnabledChanged(bool value)
    {
        _graphQlOverridesChanged();
    }

    [ObservableProperty]
    private string getCookieTemplateText = string.Empty;

    [ObservableProperty]
    private string cookieAuthorizationReturnUrlText = string.Empty;

    [ObservableProperty]
    private string graphQlEndpointUrlText = string.Empty;

    [ObservableProperty]
    private string graphQlDefaultRefererUrlText = string.Empty;

    [ObservableProperty]
    private string graphQlDefaultOriginUrlText = string.Empty;

    [ObservableProperty]
    private string graphQlTomorrowRefererUrlText = string.Empty;

    [ObservableProperty]
    private string graphQlTomorrowOriginUrlText = string.Empty;

    [ObservableProperty]
    private string tomorrowReservationQueueUrlTemplateText = string.Empty;

    [ObservableProperty]
    private string remoteCheckInAuthUrlTemplateText = string.Empty;

    [ObservableProperty]
    private string remoteCheckInAuthorizationReturnUrlText = string.Empty;

    [ObservableProperty]
    private string remoteCheckInAuthRefererUrlText = string.Empty;

    [ObservableProperty]
    private string remoteCheckInDevicesEndpointUrlText = string.Empty;

    [ObservableProperty]
    private string remoteCheckInTimeEndpointUrlText = string.Empty;

    [ObservableProperty]
    private string remoteCheckInSignEndpointUrlText = string.Empty;

    [ObservableProperty]
    private string remoteCheckInApiRefererUrlText = string.Empty;

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
    private string tomorrowReservationWarmUpTemplateText = string.Empty;

    [ObservableProperty]
    private string tomorrowReservationSaveTemplateText = string.Empty;

    [ObservableProperty]
    private string tomorrowReservationInfoTemplateText = string.Empty;

    partial void OnGetCookieTemplateTextChanged(string value) => ValidateAndScheduleAutoSave();

    partial void OnCookieAuthorizationReturnUrlTextChanged(string value) => ValidateAndScheduleAutoSave();

    partial void OnGraphQlEndpointUrlTextChanged(string value) => ValidateAndScheduleAutoSave();

    partial void OnGraphQlDefaultRefererUrlTextChanged(string value) => ValidateAndScheduleAutoSave();

    partial void OnGraphQlDefaultOriginUrlTextChanged(string value) => ValidateAndScheduleAutoSave();

    partial void OnGraphQlTomorrowRefererUrlTextChanged(string value) => ValidateAndScheduleAutoSave();

    partial void OnGraphQlTomorrowOriginUrlTextChanged(string value) => ValidateAndScheduleAutoSave();

    partial void OnTomorrowReservationQueueUrlTemplateTextChanged(string value) => ValidateAndScheduleAutoSave();

    partial void OnRemoteCheckInAuthUrlTemplateTextChanged(string value) => ValidateAndScheduleAutoSave();

    partial void OnRemoteCheckInAuthorizationReturnUrlTextChanged(string value) => ValidateAndScheduleAutoSave();

    partial void OnRemoteCheckInAuthRefererUrlTextChanged(string value) => ValidateAndScheduleAutoSave();

    partial void OnRemoteCheckInDevicesEndpointUrlTextChanged(string value) => ValidateAndScheduleAutoSave();

    partial void OnRemoteCheckInTimeEndpointUrlTextChanged(string value) => ValidateAndScheduleAutoSave();

    partial void OnRemoteCheckInSignEndpointUrlTextChanged(string value) => ValidateAndScheduleAutoSave();

    partial void OnRemoteCheckInApiRefererUrlTextChanged(string value) => ValidateAndScheduleAutoSave();

    partial void OnQueryLibrariesTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnQueryLibraryLayoutTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnQueryLibraryRuleTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnQueryReservationInfoTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnReserveSeatTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnCancelReservationTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnTomorrowReservationWarmUpTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnTomorrowReservationSaveTemplateTextChanged(string value) => ScheduleAutoSave();

    partial void OnTomorrowReservationInfoTemplateTextChanged(string value) => ScheduleAutoSave();

    public IEnumerable GetErrors(string? propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return _validationErrors.Values.SelectMany(static messages => messages).ToArray();
        }

        return _validationErrors.TryGetValue(propertyName, out var messages)
            ? messages
            : Array.Empty<string>();
    }

    public void ConfigureAutoSave(Func<bool> canAutoSave, Action graphQlOverridesChanged)
    {
        _canAutoSave = canAutoSave;
        _graphQlOverridesChanged = graphQlOverridesChanged;
    }

    [RelayCommand(CanExecute = nameof(CanSaveProtocolOverrides))]
    public async Task SaveProtocolOverridesAsync()
    {
        AutoSave.Cancel();
        await PersistAsync();
        await notificationService.ShowSuccessAsync("协议模板已保存", "TraceInt 协议覆盖已写入数据库");
    }

    [RelayCommand]
    public async Task ResetProtocolOverridesAsync()
    {
        AutoSave.Cancel();
        await protocolTemplateEditorService.ResetOverridesAsync();
        await LoadAsync();
        await notificationService.ShowSuccessAsync("协议模板已重置", "已恢复内置默认协议");
    }

    public Task PersistAsync(CancellationToken cancellationToken = default)
    {
        TraceIntProtocolValidator.EnsureValid(BuildTemplatesSnapshot());
        return protocolTemplateEditorService.SaveOverridesAsync(BuildOverridesSnapshot(), cancellationToken);
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        _isLoading = true;
        try
        {
            _defaultTemplates = await protocolTemplateEditorService.LoadDefaultTemplatesAsync(cancellationToken);
            var templates = await protocolTemplateEditorService.LoadTemplatesAsync(cancellationToken);
            GetCookieTemplateText = templates.GetCookieUrlTemplate;
            CookieAuthorizationReturnUrlText = templates.CookieAuthorizationReturnUrl;
            GraphQlEndpointUrlText = templates.GraphQlEndpointUrl;
            GraphQlDefaultRefererUrlText = templates.GraphQlDefaultRefererUrl;
            GraphQlDefaultOriginUrlText = templates.GraphQlDefaultOriginUrl;
            GraphQlTomorrowRefererUrlText = templates.GraphQlTomorrowRefererUrl;
            GraphQlTomorrowOriginUrlText = templates.GraphQlTomorrowOriginUrl;
            TomorrowReservationQueueUrlTemplateText = templates.TomorrowReservationQueueUrlTemplate;
            RemoteCheckInAuthUrlTemplateText = templates.RemoteCheckInAuthUrlTemplate;
            RemoteCheckInAuthorizationReturnUrlText = templates.RemoteCheckInAuthorizationReturnUrl;
            RemoteCheckInAuthRefererUrlText = templates.RemoteCheckInAuthRefererUrl;
            RemoteCheckInDevicesEndpointUrlText = templates.RemoteCheckInDevicesEndpointUrl;
            RemoteCheckInTimeEndpointUrlText = templates.RemoteCheckInTimeEndpointUrl;
            RemoteCheckInSignEndpointUrlText = templates.RemoteCheckInSignEndpointUrl;
            RemoteCheckInApiRefererUrlText = templates.RemoteCheckInApiRefererUrl;
            QueryLibrariesTemplateText = templates.QueryLibrariesTemplate;
            QueryLibraryLayoutTemplateText = templates.QueryLibraryLayoutTemplate;
            QueryLibraryRuleTemplateText = templates.QueryLibraryRuleTemplate;
            QueryReservationInfoTemplateText = templates.QueryReservationInfoTemplate;
            ReserveSeatTemplateText = templates.ReserveSeatTemplate;
            CancelReservationTemplateText = templates.CancelReservationTemplate;
            TomorrowReservationWarmUpTemplateText = templates.TomorrowReservationWarmUpTemplate;
            TomorrowReservationSaveTemplateText = templates.TomorrowReservationSaveTemplate;
            TomorrowReservationInfoTemplateText = templates.TomorrowReservationInfoTemplate;
        }
        finally
        {
            _isLoading = false;
            ValidateProtocolAddresses();
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

    private bool CanSaveProtocolOverrides() => !HasErrors;

    private TraceIntProtocolTemplates BuildTemplatesSnapshot()
    {
        return new TraceIntProtocolTemplates
        {
            GetCookieUrlTemplate = GetCookieTemplateText,
            CookieAuthorizationReturnUrl = CookieAuthorizationReturnUrlText,
            GraphQlEndpointUrl = GraphQlEndpointUrlText,
            GraphQlDefaultRefererUrl = GraphQlDefaultRefererUrlText,
            GraphQlDefaultOriginUrl = GraphQlDefaultOriginUrlText,
            GraphQlTomorrowRefererUrl = GraphQlTomorrowRefererUrlText,
            GraphQlTomorrowOriginUrl = GraphQlTomorrowOriginUrlText,
            TomorrowReservationQueueUrlTemplate = TomorrowReservationQueueUrlTemplateText,
            RemoteCheckInAuthUrlTemplate = RemoteCheckInAuthUrlTemplateText,
            RemoteCheckInAuthorizationReturnUrl = RemoteCheckInAuthorizationReturnUrlText,
            RemoteCheckInAuthRefererUrl = RemoteCheckInAuthRefererUrlText,
            RemoteCheckInDevicesEndpointUrl = RemoteCheckInDevicesEndpointUrlText,
            RemoteCheckInTimeEndpointUrl = RemoteCheckInTimeEndpointUrlText,
            RemoteCheckInSignEndpointUrl = RemoteCheckInSignEndpointUrlText,
            RemoteCheckInApiRefererUrl = RemoteCheckInApiRefererUrlText,
            QueryLibrariesTemplate = QueryLibrariesTemplateText,
            QueryLibraryLayoutTemplate = QueryLibraryLayoutTemplateText,
            QueryLibraryRuleTemplate = QueryLibraryRuleTemplateText,
            QueryReservationInfoTemplate = QueryReservationInfoTemplateText,
            ReserveSeatTemplate = ReserveSeatTemplateText,
            CancelReservationTemplate = CancelReservationTemplateText,
            TomorrowReservationWarmUpTemplate = TomorrowReservationWarmUpTemplateText,
            TomorrowReservationSaveTemplate = TomorrowReservationSaveTemplateText,
            TomorrowReservationInfoTemplate = TomorrowReservationInfoTemplateText
        };
    }

    private TraceIntProtocolTemplateOverrides BuildOverridesSnapshot()
    {
        var current = BuildTemplatesSnapshot();
        if (_defaultTemplates is not null)
        {
            return TraceIntProtocolTemplateOverrides.FromDifferences(current, _defaultTemplates);
        }

        return new TraceIntProtocolTemplateOverrides
        {
            GetCookieUrlTemplate = current.GetCookieUrlTemplate,
            CookieAuthorizationReturnUrl = current.CookieAuthorizationReturnUrl,
            GraphQlEndpointUrl = current.GraphQlEndpointUrl,
            GraphQlDefaultRefererUrl = current.GraphQlDefaultRefererUrl,
            GraphQlDefaultOriginUrl = current.GraphQlDefaultOriginUrl,
            GraphQlTomorrowRefererUrl = current.GraphQlTomorrowRefererUrl,
            GraphQlTomorrowOriginUrl = current.GraphQlTomorrowOriginUrl,
            TomorrowReservationQueueUrlTemplate = current.TomorrowReservationQueueUrlTemplate,
            RemoteCheckInAuthUrlTemplate = current.RemoteCheckInAuthUrlTemplate,
            RemoteCheckInAuthorizationReturnUrl = current.RemoteCheckInAuthorizationReturnUrl,
            RemoteCheckInAuthRefererUrl = current.RemoteCheckInAuthRefererUrl,
            RemoteCheckInDevicesEndpointUrl = current.RemoteCheckInDevicesEndpointUrl,
            RemoteCheckInTimeEndpointUrl = current.RemoteCheckInTimeEndpointUrl,
            RemoteCheckInSignEndpointUrl = current.RemoteCheckInSignEndpointUrl,
            RemoteCheckInApiRefererUrl = current.RemoteCheckInApiRefererUrl,
            QueryLibrariesTemplate = current.QueryLibrariesTemplate,
            QueryLibraryLayoutTemplate = current.QueryLibraryLayoutTemplate,
            QueryLibraryRuleTemplate = current.QueryLibraryRuleTemplate,
            QueryReservationInfoTemplate = current.QueryReservationInfoTemplate,
            ReserveSeatTemplate = current.ReserveSeatTemplate,
            CancelReservationTemplate = current.CancelReservationTemplate,
            TomorrowReservationWarmUpTemplate = current.TomorrowReservationWarmUpTemplate,
            TomorrowReservationSaveTemplate = current.TomorrowReservationSaveTemplate,
            TomorrowReservationInfoTemplate = current.TomorrowReservationInfoTemplate
        };
    }

    private void ValidateAndScheduleAutoSave()
    {
        if (_isLoading)
        {
            return;
        }

        ValidateProtocolAddresses();
        ScheduleAutoSave();
    }

    private void ValidateProtocolAddresses()
    {
        var previousProperties = _validationErrors.Keys.ToHashSet(StringComparer.Ordinal);
        _validationResult = TraceIntProtocolValidator.Validate(BuildTemplatesSnapshot());
        _validationErrors.Clear();
        foreach (var group in _validationResult.Errors.GroupBy(static issue => MapToViewModelProperty(issue.PropertyName)))
        {
            _validationErrors[group.Key] = group.Select(static issue => issue.Message).Distinct().ToArray();
        }

        foreach (var propertyName in previousProperties.Union(_validationErrors.Keys, StringComparer.Ordinal))
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        }

        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasProtocolValidationErrors));
        OnPropertyChanged(nameof(HasProtocolValidationWarnings));
        OnPropertyChanged(nameof(ProtocolValidationSummaryText));
        OnPropertyChanged(nameof(ProtocolValidationWarningText));
        SaveProtocolOverridesCommand.NotifyCanExecuteChanged();

        if (HasErrors)
        {
            AutoSave.Cancel();
        }
    }

    private void ScheduleAutoSave()
    {
        if (_isLoading || HasErrors || !TraceIntGraphQlOverridesEnabled || !_canAutoSave())
        {
            return;
        }

        AutoSave.Schedule(ex =>
            activityLogService.Write(LogEntryKind.Warning, "Settings", $"自动保存 TraceInt 协议失败：{ex.Message}", ex));
    }

    private static string MapToViewModelProperty(string propertyName)
    {
        return propertyName switch
        {
            nameof(TraceIntProtocolTemplates.GetCookieUrlTemplate) => nameof(GetCookieTemplateText),
            nameof(TraceIntProtocolTemplates.CookieAuthorizationReturnUrl) => nameof(CookieAuthorizationReturnUrlText),
            nameof(TraceIntProtocolTemplates.GraphQlEndpointUrl) => nameof(GraphQlEndpointUrlText),
            nameof(TraceIntProtocolTemplates.GraphQlDefaultRefererUrl) => nameof(GraphQlDefaultRefererUrlText),
            nameof(TraceIntProtocolTemplates.GraphQlDefaultOriginUrl) => nameof(GraphQlDefaultOriginUrlText),
            nameof(TraceIntProtocolTemplates.GraphQlTomorrowRefererUrl) => nameof(GraphQlTomorrowRefererUrlText),
            nameof(TraceIntProtocolTemplates.GraphQlTomorrowOriginUrl) => nameof(GraphQlTomorrowOriginUrlText),
            nameof(TraceIntProtocolTemplates.TomorrowReservationQueueUrlTemplate) => nameof(TomorrowReservationQueueUrlTemplateText),
            nameof(TraceIntProtocolTemplates.RemoteCheckInAuthUrlTemplate) => nameof(RemoteCheckInAuthUrlTemplateText),
            nameof(TraceIntProtocolTemplates.RemoteCheckInAuthorizationReturnUrl) => nameof(RemoteCheckInAuthorizationReturnUrlText),
            nameof(TraceIntProtocolTemplates.RemoteCheckInAuthRefererUrl) => nameof(RemoteCheckInAuthRefererUrlText),
            nameof(TraceIntProtocolTemplates.RemoteCheckInDevicesEndpointUrl) => nameof(RemoteCheckInDevicesEndpointUrlText),
            nameof(TraceIntProtocolTemplates.RemoteCheckInTimeEndpointUrl) => nameof(RemoteCheckInTimeEndpointUrlText),
            nameof(TraceIntProtocolTemplates.RemoteCheckInSignEndpointUrl) => nameof(RemoteCheckInSignEndpointUrlText),
            nameof(TraceIntProtocolTemplates.RemoteCheckInApiRefererUrl) => nameof(RemoteCheckInApiRefererUrlText),
            _ => propertyName
        };
    }
}
