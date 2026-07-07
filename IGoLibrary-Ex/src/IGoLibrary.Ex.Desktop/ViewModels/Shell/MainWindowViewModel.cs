using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed class MainWindowViewModel(
    MainWindowWorkflowPages pages,
    ShellWorkflowState workflowState,
    IActivityLogService activityLogService,
    INotificationService notificationService,
    IAppThemeService appThemeService,
    TimeProvider timeProvider,
    ILanCookieRelayService lanCookieRelayService,
    IMobileControlService mobileControlService)
    : MainWindowWorkflowViewModel(
        pages,
        workflowState,
        activityLogService,
        notificationService,
        appThemeService,
        timeProvider,
        lanCookieRelayService,
        mobileControlService);
