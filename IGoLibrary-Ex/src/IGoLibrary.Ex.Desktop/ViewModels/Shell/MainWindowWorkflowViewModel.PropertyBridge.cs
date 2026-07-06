namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    partial void ConfigurePropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        ConfigureUpdateLinksPropertyBridge(propertyBridge);
        ConfigureSystemSettingsPropertyBridge(propertyBridge);
        ConfigureProtocolTemplatesPropertyBridge(propertyBridge);
        ConfigureNotificationSettingsPropertyBridge(propertyBridge);
        ConfigureNavigationPropertyBridge(propertyBridge);
        ConfigureActivityLogsPropertyBridge(propertyBridge);
        ConfigureSessionPropertyBridge(propertyBridge);
        ConfigureHomeDashboardPropertyBridge(propertyBridge);
        ConfigureAccountVenuePropertyBridge(propertyBridge);
        ConfigureMultiSeatSelectionPropertyBridge(propertyBridge);
        ConfigureGrabPropertyBridge(propertyBridge);
        ConfigureGlobalLeakPropertyBridge(propertyBridge);
        ConfigureTomorrowReservationPropertyBridge(propertyBridge);
        ConfigureLanCookieRelayPropertyBridge(propertyBridge);
        ConfigureOccupyPropertyBridge(propertyBridge);
    }
}
