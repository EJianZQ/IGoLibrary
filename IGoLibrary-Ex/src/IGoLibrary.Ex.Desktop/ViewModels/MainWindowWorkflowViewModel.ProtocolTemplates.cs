using CommunityToolkit.Mvvm.Input;

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
}
