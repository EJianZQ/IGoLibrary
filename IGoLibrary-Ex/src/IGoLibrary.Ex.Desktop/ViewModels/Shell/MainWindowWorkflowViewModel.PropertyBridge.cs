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
        ConfigureMobileControlPropertyBridge(propertyBridge);
        ConfigureOccupyPropertyBridge(propertyBridge);
        ConfigureModalOverlayPropertyBridge(propertyBridge);
    }

    private void ConfigureModalOverlayPropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        string[] targetPropertyNames =
        [
            nameof(HasOpenModalOverlay),
            nameof(IsSidebarNavigationInteractive)
        ];

        propertyBridge.Forward(
            LanCookieRelay,
            nameof(LanCookieRelay.IsLanCookieRelayDialogOpen),
            targetPropertyNames);
        propertyBridge.Forward(
            GrabPage,
            nameof(GrabPage.IsGrabSeatSelectionOverlayOpen),
            targetPropertyNames);
        propertyBridge.Forward(
            MobileControl,
            nameof(MobileControl.IsMobileControlDetailsOpen),
            targetPropertyNames);
        propertyBridge.Forward(
            GlobalLeakPage,
            nameof(GlobalLeakPage.IsGlobalLeakLibraryPickerOpen),
            targetPropertyNames);
        propertyBridge.Forward(
            TomorrowReservationPage,
            nameof(TomorrowReservationPage.IsTomorrowSeatSelectionOverlayOpen),
            targetPropertyNames);
        propertyBridge.Forward(
            AccountVenue,
            nameof(AccountVenue.IsVenuePickerOpen),
            targetPropertyNames);
    }
}
