using System.Collections.ObjectModel;
using System.Diagnostics;
using System.ComponentModel;
using System.Globalization;
using System.Text;
using Avalonia.Threading;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Services;
using IGoLibrary.Ex.Desktop.Platform;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel(
    MainWindowWorkflowPages pages,
    ShellWorkflowState workflowState,
    IActivityLogService activityLogService,
    INotificationService notificationService,
    IAppThemeService appThemeService,
    TimeProvider timeProvider,
    ILanCookieRelayService lanCookieRelayService) : ViewModelBase
{
    private readonly IAppThemeService _appThemeService = appThemeService;

    private readonly IActivityLogService _activityLogService = activityLogService;

    private readonly INotificationService _notificationService = notificationService;

    private readonly ILanCookieRelayService _lanCookieRelayService = lanCookieRelayService;

    private readonly TimeProvider _timeProvider = timeProvider;

    private ViewModelPropertyBridge? _propertyBridge;

    public ShellWorkflowState WorkflowState { get; } = workflowState;

    public MainWindowWorkflowPages Pages { get; } = pages;

    public HomeDashboardViewModel HomeDashboard => Pages.HomeDashboard;

    public SessionViewModel Session => Pages.Session;

    public AccountVenueViewModel AccountVenue => Pages.AccountVenue;

    public MultiSeatSelectionViewModel MultiSeatSelection => Pages.MultiSeatSelection;

    public GrabPageViewModel GrabPage => Pages.GrabPage;

    public GlobalLeakPageViewModel GlobalLeakPage => Pages.GlobalLeakPage;

    public OccupyPageViewModel OccupyPage => Pages.OccupyPage;

    public TomorrowReservationPageViewModel TomorrowReservationPage => Pages.TomorrowReservationPage;

    public LanCookieRelayViewModel LanCookieRelay => Pages.LanCookieRelay;

    public NotificationSettingsViewModel NotificationSettings => Pages.NotificationSettings;

    public SystemSettingsViewModel SystemSettings => Pages.SystemSettings;

    public ProtocolTemplatesViewModel ProtocolTemplates => Pages.ProtocolTemplates;

    public ShellNavigationViewModel Navigation => Pages.Navigation;

    public ActivityLogPanelViewModel ActivityLogs => Pages.ActivityLogs;

    public UpdateLinksViewModel UpdateLinks => Pages.UpdateLinks;

    partial void ConfigurePropertyBridge(ViewModelPropertyBridge propertyBridge);

    private void EnsurePropertyBridge()
    {
        if (_propertyBridge is not null)
        {
            return;
        }

        _propertyBridge = new ViewModelPropertyBridge(OnPropertyChanged);
        ConfigurePropertyBridge(_propertyBridge);
    }

    private DateTimeOffset GetCurrentTime()
    {
        return _timeProvider.GetUtcNow().ToLocalTime();
    }
}
