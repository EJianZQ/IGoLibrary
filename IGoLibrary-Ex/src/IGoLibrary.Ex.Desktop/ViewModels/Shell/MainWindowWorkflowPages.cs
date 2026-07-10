namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed class MainWindowWorkflowPages(
    HomeDashboardViewModel homeDashboard,
    SessionViewModel session,
    AccountVenueViewModel accountVenue,
    MultiSeatSelectionViewModel multiSeatSelection,
    GrabPageViewModel grabPage,
    GlobalLeakPageViewModel globalLeakPage,
    OccupyPageViewModel occupyPage,
    TomorrowReservationPageViewModel tomorrowReservationPage,
    LanCookieRelayViewModel lanCookieRelay,
    RemoteCheckInPageViewModel remoteCheckInPage,
    MobileControlPageViewModel mobileControl,
    NotificationSettingsViewModel notificationSettings,
    SystemSettingsViewModel systemSettings,
    ProtocolTemplatesViewModel protocolTemplates,
    ShellNavigationViewModel navigation,
    ActivityLogPanelViewModel activityLogs,
    UpdateLinksViewModel updateLinks)
{
    public HomeDashboardViewModel HomeDashboard { get; } = homeDashboard;

    public SessionViewModel Session { get; } = session;

    public AccountVenueViewModel AccountVenue { get; } = accountVenue;

    public MultiSeatSelectionViewModel MultiSeatSelection { get; } = multiSeatSelection;

    public GrabPageViewModel GrabPage { get; } = grabPage;

    public GlobalLeakPageViewModel GlobalLeakPage { get; } = globalLeakPage;

    public OccupyPageViewModel OccupyPage { get; } = occupyPage;

    public TomorrowReservationPageViewModel TomorrowReservationPage { get; } = tomorrowReservationPage;

    public LanCookieRelayViewModel LanCookieRelay { get; } = lanCookieRelay;

    public RemoteCheckInPageViewModel RemoteCheckInPage { get; } = remoteCheckInPage;

    public MobileControlPageViewModel MobileControl { get; } = mobileControl;

    public NotificationSettingsViewModel NotificationSettings { get; } = notificationSettings;

    public SystemSettingsViewModel SystemSettings { get; } = systemSettings;

    public ProtocolTemplatesViewModel ProtocolTemplates { get; } = protocolTemplates;

    public ShellNavigationViewModel Navigation { get; } = navigation;

    public ActivityLogPanelViewModel ActivityLogs { get; } = activityLogs;

    public UpdateLinksViewModel UpdateLinks { get; } = updateLinks;
}
