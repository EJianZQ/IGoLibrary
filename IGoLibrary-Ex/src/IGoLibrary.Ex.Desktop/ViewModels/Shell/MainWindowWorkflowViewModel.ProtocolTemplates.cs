using CommunityToolkit.Mvvm.Input;
using System.Collections;
using System.ComponentModel;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    private bool _protocolTemplatesConfigured;

    public bool TraceIntGraphQlOverridesEnabled
    {
        get => ProtocolTemplates.TraceIntGraphQlOverridesEnabled;
        set
        {
            EnsureProtocolTemplatesConfigured();
            ProtocolTemplates.TraceIntGraphQlOverridesEnabled = value;
        }
    }

    public string GetCookieTemplateText
    {
        get => ProtocolTemplates.GetCookieTemplateText;
        set => ProtocolTemplates.GetCookieTemplateText = value;
    }

    public string CookieAuthorizationReturnUrlText
    {
        get => ProtocolTemplates.CookieAuthorizationReturnUrlText;
        set => ProtocolTemplates.CookieAuthorizationReturnUrlText = value;
    }

    public string GraphQlEndpointUrlText
    {
        get => ProtocolTemplates.GraphQlEndpointUrlText;
        set => ProtocolTemplates.GraphQlEndpointUrlText = value;
    }

    public string GraphQlDefaultRefererUrlText
    {
        get => ProtocolTemplates.GraphQlDefaultRefererUrlText;
        set => ProtocolTemplates.GraphQlDefaultRefererUrlText = value;
    }

    public string GraphQlDefaultOriginUrlText
    {
        get => ProtocolTemplates.GraphQlDefaultOriginUrlText;
        set => ProtocolTemplates.GraphQlDefaultOriginUrlText = value;
    }

    public string GraphQlTomorrowRefererUrlText
    {
        get => ProtocolTemplates.GraphQlTomorrowRefererUrlText;
        set => ProtocolTemplates.GraphQlTomorrowRefererUrlText = value;
    }

    public string GraphQlTomorrowOriginUrlText
    {
        get => ProtocolTemplates.GraphQlTomorrowOriginUrlText;
        set => ProtocolTemplates.GraphQlTomorrowOriginUrlText = value;
    }

    public string QueryLibrariesTemplateText
    {
        get => ProtocolTemplates.QueryLibrariesTemplateText;
        set => ProtocolTemplates.QueryLibrariesTemplateText = value;
    }

    public string QueryLibraryLayoutTemplateText
    {
        get => ProtocolTemplates.QueryLibraryLayoutTemplateText;
        set => ProtocolTemplates.QueryLibraryLayoutTemplateText = value;
    }

    public string QueryLibraryRuleTemplateText
    {
        get => ProtocolTemplates.QueryLibraryRuleTemplateText;
        set => ProtocolTemplates.QueryLibraryRuleTemplateText = value;
    }

    public string QueryReservationInfoTemplateText
    {
        get => ProtocolTemplates.QueryReservationInfoTemplateText;
        set => ProtocolTemplates.QueryReservationInfoTemplateText = value;
    }

    public string ReserveSeatTemplateText
    {
        get => ProtocolTemplates.ReserveSeatTemplateText;
        set => ProtocolTemplates.ReserveSeatTemplateText = value;
    }

    public string CancelReservationTemplateText
    {
        get => ProtocolTemplates.CancelReservationTemplateText;
        set => ProtocolTemplates.CancelReservationTemplateText = value;
    }

    public string TomorrowReservationQueueUrlTemplateText
    {
        get => ProtocolTemplates.TomorrowReservationQueueUrlTemplateText;
        set => ProtocolTemplates.TomorrowReservationQueueUrlTemplateText = value;
    }

    public string RemoteCheckInAuthUrlTemplateText
    {
        get => ProtocolTemplates.RemoteCheckInAuthUrlTemplateText;
        set => ProtocolTemplates.RemoteCheckInAuthUrlTemplateText = value;
    }

    public string RemoteCheckInAuthorizationReturnUrlText
    {
        get => ProtocolTemplates.RemoteCheckInAuthorizationReturnUrlText;
        set => ProtocolTemplates.RemoteCheckInAuthorizationReturnUrlText = value;
    }

    public string RemoteCheckInAuthRefererUrlText
    {
        get => ProtocolTemplates.RemoteCheckInAuthRefererUrlText;
        set => ProtocolTemplates.RemoteCheckInAuthRefererUrlText = value;
    }

    public string RemoteCheckInDevicesEndpointUrlText
    {
        get => ProtocolTemplates.RemoteCheckInDevicesEndpointUrlText;
        set => ProtocolTemplates.RemoteCheckInDevicesEndpointUrlText = value;
    }

    public string RemoteCheckInTimeEndpointUrlText
    {
        get => ProtocolTemplates.RemoteCheckInTimeEndpointUrlText;
        set => ProtocolTemplates.RemoteCheckInTimeEndpointUrlText = value;
    }

    public string RemoteCheckInSignEndpointUrlText
    {
        get => ProtocolTemplates.RemoteCheckInSignEndpointUrlText;
        set => ProtocolTemplates.RemoteCheckInSignEndpointUrlText = value;
    }

    public string RemoteCheckInApiRefererUrlText
    {
        get => ProtocolTemplates.RemoteCheckInApiRefererUrlText;
        set => ProtocolTemplates.RemoteCheckInApiRefererUrlText = value;
    }

    public bool HasErrors => ProtocolTemplates.HasErrors;

    public bool HasProtocolValidationErrors => ProtocolTemplates.HasProtocolValidationErrors;

    public bool HasProtocolValidationWarnings => ProtocolTemplates.HasProtocolValidationWarnings;

    public string ProtocolValidationSummaryText => ProtocolTemplates.ProtocolValidationSummaryText;

    public string ProtocolValidationWarningText => ProtocolTemplates.ProtocolValidationWarningText;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName) => ProtocolTemplates.GetErrors(propertyName);

    public string TomorrowReservationWarmUpTemplateText
    {
        get => ProtocolTemplates.TomorrowReservationWarmUpTemplateText;
        set => ProtocolTemplates.TomorrowReservationWarmUpTemplateText = value;
    }

    public string TomorrowReservationSaveTemplateText
    {
        get => ProtocolTemplates.TomorrowReservationSaveTemplateText;
        set => ProtocolTemplates.TomorrowReservationSaveTemplateText = value;
    }

    public string TomorrowReservationInfoTemplateText
    {
        get => ProtocolTemplates.TomorrowReservationInfoTemplateText;
        set => ProtocolTemplates.TomorrowReservationInfoTemplateText = value;
    }

    public IAsyncRelayCommand SaveProtocolOverridesCommand => ProtocolTemplates.SaveProtocolOverridesCommand;

    public IAsyncRelayCommand ResetProtocolOverridesCommand => ProtocolTemplates.ResetProtocolOverridesCommand;

    private void ConfigureProtocolTemplatesPropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        propertyBridge.Forward(
            ProtocolTemplates,
            nameof(ProtocolTemplatesViewModel.TraceIntGraphQlOverridesEnabled),
            nameof(TraceIntGraphQlOverridesEnabled));
        propertyBridge.Forward(
            ProtocolTemplates,
            nameof(ProtocolTemplatesViewModel.GetCookieTemplateText),
            nameof(GetCookieTemplateText));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.CookieAuthorizationReturnUrlText), nameof(CookieAuthorizationReturnUrlText));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.GraphQlEndpointUrlText), nameof(GraphQlEndpointUrlText));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.GraphQlDefaultRefererUrlText), nameof(GraphQlDefaultRefererUrlText));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.GraphQlDefaultOriginUrlText), nameof(GraphQlDefaultOriginUrlText));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.GraphQlTomorrowRefererUrlText), nameof(GraphQlTomorrowRefererUrlText));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.GraphQlTomorrowOriginUrlText), nameof(GraphQlTomorrowOriginUrlText));
        propertyBridge.Forward(
            ProtocolTemplates,
            nameof(ProtocolTemplatesViewModel.QueryLibrariesTemplateText),
            nameof(QueryLibrariesTemplateText));
        propertyBridge.Forward(
            ProtocolTemplates,
            nameof(ProtocolTemplatesViewModel.QueryLibraryLayoutTemplateText),
            nameof(QueryLibraryLayoutTemplateText));
        propertyBridge.Forward(
            ProtocolTemplates,
            nameof(ProtocolTemplatesViewModel.QueryLibraryRuleTemplateText),
            nameof(QueryLibraryRuleTemplateText));
        propertyBridge.Forward(
            ProtocolTemplates,
            nameof(ProtocolTemplatesViewModel.QueryReservationInfoTemplateText),
            nameof(QueryReservationInfoTemplateText));
        propertyBridge.Forward(
            ProtocolTemplates,
            nameof(ProtocolTemplatesViewModel.ReserveSeatTemplateText),
            nameof(ReserveSeatTemplateText));
        propertyBridge.Forward(
            ProtocolTemplates,
            nameof(ProtocolTemplatesViewModel.CancelReservationTemplateText),
            nameof(CancelReservationTemplateText));
        propertyBridge.Forward(
            ProtocolTemplates,
            nameof(ProtocolTemplatesViewModel.TomorrowReservationQueueUrlTemplateText),
            nameof(TomorrowReservationQueueUrlTemplateText));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.RemoteCheckInAuthUrlTemplateText), nameof(RemoteCheckInAuthUrlTemplateText));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.RemoteCheckInAuthorizationReturnUrlText), nameof(RemoteCheckInAuthorizationReturnUrlText));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.RemoteCheckInAuthRefererUrlText), nameof(RemoteCheckInAuthRefererUrlText));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.RemoteCheckInDevicesEndpointUrlText), nameof(RemoteCheckInDevicesEndpointUrlText));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.RemoteCheckInTimeEndpointUrlText), nameof(RemoteCheckInTimeEndpointUrlText));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.RemoteCheckInSignEndpointUrlText), nameof(RemoteCheckInSignEndpointUrlText));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.RemoteCheckInApiRefererUrlText), nameof(RemoteCheckInApiRefererUrlText));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.HasErrors), nameof(HasErrors));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.HasProtocolValidationErrors), nameof(HasProtocolValidationErrors));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.HasProtocolValidationWarnings), nameof(HasProtocolValidationWarnings));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.ProtocolValidationSummaryText), nameof(ProtocolValidationSummaryText));
        propertyBridge.Forward(ProtocolTemplates, nameof(ProtocolTemplatesViewModel.ProtocolValidationWarningText), nameof(ProtocolValidationWarningText));
        propertyBridge.Forward(
            ProtocolTemplates,
            nameof(ProtocolTemplatesViewModel.TomorrowReservationWarmUpTemplateText),
            nameof(TomorrowReservationWarmUpTemplateText));
        propertyBridge.Forward(
            ProtocolTemplates,
            nameof(ProtocolTemplatesViewModel.TomorrowReservationSaveTemplateText),
            nameof(TomorrowReservationSaveTemplateText));
        propertyBridge.Forward(
            ProtocolTemplates,
            nameof(ProtocolTemplatesViewModel.TomorrowReservationInfoTemplateText),
            nameof(TomorrowReservationInfoTemplateText));
    }

    private void EnsureProtocolTemplatesConfigured()
    {
        if (_protocolTemplatesConfigured)
        {
            return;
        }

        ProtocolTemplates.ConfigureAutoSave(
            () => !IsLoadingSettings && IsInitializationComplete,
            ScheduleSystemSettingsAutoSave);
        ProtocolTemplates.ErrorsChanged += OnProtocolTemplateErrorsChanged;
        _protocolTemplatesConfigured = true;
    }

    private Task PersistProtocolOverridesAsync(CancellationToken cancellationToken = default)
    {
        EnsureProtocolTemplatesConfigured();
        return ProtocolTemplates.PersistAsync(cancellationToken);
    }

    private async Task LoadProtocolTemplatesAsync()
    {
        EnsureProtocolTemplatesConfigured();
        await ProtocolTemplates.LoadAsync();
    }

    private void ScheduleProtocolTemplateAutoSave()
    {
        EnsureProtocolTemplatesConfigured();
    }

    private void CancelPendingProtocolTemplateAutoSave()
    {
        ProtocolTemplates.CancelPendingAutoSave();
    }

    private bool HasPendingProtocolTemplateAutoSave => ProtocolTemplates.HasPendingAutoSave;

    private void OnProtocolTemplateErrorsChanged(object? sender, DataErrorsChangedEventArgs e)
    {
        ErrorsChanged?.Invoke(this, e);
    }
}
