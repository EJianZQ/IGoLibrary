using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Desktop.Services;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class ShellNavigationViewModel(
    AppWindowService appWindowService,
    IGrabSeatCoordinator grabSeatCoordinator,
    IGlobalLeakCoordinator globalLeakCoordinator,
    IOccupySeatCoordinator occupySeatCoordinator,
    ITomorrowReservationCoordinator tomorrowReservationCoordinator) : ViewModelBase
{
    private const int GlobalLeakTabIndex = 3;
    private const int TomorrowReservationTabIndex = 4;
    private const int OccupyTabIndex = 5;
    public const int RemoteCheckInTabIndex = 6;
    private const int MobileControlTabIndex = 7;
    private const int NotificationSettingsTabIndex = 8;
    private const int SystemSettingsTabIndex = 9;

    private static readonly SidebarNavigationItem HomeSidebarItem = new(
        0,
        "首页",
        "M12 3L2 12h3v8h6v-6h2v6h6v-8h3L12 3z");

    private static readonly SidebarNavigationItem AccountAndVenueSidebarItem = new(
        AccountAndVenueTabIndex,
        "账户与场馆",
        "M12 12c2.21 0 4-1.79 4-4s-1.79-4-4-4-4 1.79-4 4 1.79 4 4 4zm0 2c-2.67 0-8 1.34-8 4v2h16v-2c0-2.66-5.33-4-8-4z");

    private static readonly SidebarNavigationItem GrabSidebarItem = new(
        2,
        "抢座",
        "M7 2v11h3v9l7-12h-4l4-8z");

    private static readonly SidebarNavigationItem GlobalLeakSidebarItem = new(
        GlobalLeakTabIndex,
        "全域捡漏",
        "M9.5 3a6.5 6.5 0 0 1 5.17 10.43l4.45 4.45-1.41 1.41-4.45-4.45A6.5 6.5 0 1 1 9.5 3zm0 2a4.5 4.5 0 1 0 0 9 4.5 4.5 0 0 0 0-9zm9.5-1h2v5h-2V4zm0 7h2v2h-2v-2z");

    private static readonly SidebarNavigationItem TomorrowReservationSidebarItem = new(
        TomorrowReservationTabIndex,
        "明日预约",
        "M19 3h-1V1h-2v2H8V1H6v2H5c-1.1 0-2 .9-2 2v14c0 1.1.9 2 2 2h14c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 16H5V8h14v11zM7 10h5v5H7z");

    private static readonly SidebarNavigationItem OccupySidebarItem = new(
        OccupyTabIndex,
        "占座",
        "M11.99 2C6.47 2 2 6.48 2 12s4.47 10 9.99 10C17.52 22 22 17.52 22 12S17.52 2 11.99 2zM12 20c-4.42 0-8-3.58-8-8s3.58-8 8-8 8 3.58 8 8-3.58 8-8 8z M12.5 7H11v6l5.25 3.15.75-1.23-4.5-2.67z");

    private static readonly SidebarNavigationItem MobileControlSidebarItem = new(
        MobileControlTabIndex,
        "手机控制",
        "M17 1H7C5.9 1 5 1.9 5 3v18c0 1.1.9 2 2 2h10c1.1 0 2-.9 2-2V3c0-1.1-.9-2-2-2zm0 17H7V4h10v14zm-3 3h-4v-1h4v1z");

    private static readonly SidebarNavigationItem RemoteCheckInSidebarItem = new(
        RemoteCheckInTabIndex,
        "远程签到",
        "M12 2a5 5 0 0 0-5 5v2H5a2 2 0 0 0-2 2v9a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-9a2 2 0 0 0-2-2h-2V7a5 5 0 0 0-5-5zm-3 7V7a3 3 0 0 1 6 0v2H9zm3 3 4 4-1.4 1.4-1.6-1.6V20h-2v-4.2l-1.6 1.6L8 16l4-4z");

    private static readonly SidebarNavigationItem NotificationSettingsSidebarItem = new(
        NotificationSettingsTabIndex,
        "自动通知",
        "M12 22a2.5 2.5 0 0 0 2.45-2h-4.9A2.5 2.5 0 0 0 12 22zm6-6V11a6 6 0 1 0-12 0v5l-2 2v1h16v-1l-2-2z");

    private static readonly SidebarNavigationItem SettingsSidebarItem = new(
        SystemSettingsTabIndex,
        "系统设置",
        "M19.14,12.94c0.04-0.3,0.06-0.61,0.06-0.94c0-0.32-0.02-0.64-0.06-0.94l2.03-1.58c0.18-0.14,0.23-0.41,0.12-0.61 l-1.92-3.32c-0.12-0.22-0.37-0.29-0.59-0.22l-2.39,0.96c-0.5-0.38-1.03-0.7-1.62-0.94L14.4,2.81c-0.04-0.24-0.24-0.41-0.48-0.41 h-3.84c-0.24,0-0.43,0.17-0.47,0.41L9.25,5.35C8.66,5.59,8.12,5.92,7.63,6.29L5.24,5.33c-0.22-0.08-0.47,0-0.59,0.22L2.73,8.87 C2.62,9.08,2.66,9.34,2.86,9.48l2.03,1.58C4.84,11.36,4.8,11.69,4.8,12s0.02,0.64,0.06,0.94l-2.03,1.58 c-0.18,0.14-0.23,0.41-0.12,0.61l1.92,3.32c0.12,0.22,0.37,0.29,0.59,0.22l2.39-0.96c0.5,0.38,1.03,0.7,1.62,0.94l0.36,2.54 c0.05,0.24,0.24,0.41,0.48,0.41h3.84c0.24,0,0.43-0.17,0.47-0.41l0.36-2.54c0.59-0.24,1.13-0.56,1.62-0.94l2.39,0.96 c0.22,0.08,0.47,0,0.59-0.22l1.92-3.32c0.12-0.22,0.07-0.49-0.12-0.61L19.14,12.94z M12,15.6c-1.98,0-3.6-1.62-3.6-3.6 s1.62-3.6,3.6-3.6s3.6,1.62,3.6,3.6S13.98,15.6,12,15.6z");

    private static readonly SidebarNavigationItem[] UnauthorizedSidebarItems =
    [
        HomeSidebarItem,
        AccountAndVenueSidebarItem,
        SettingsSidebarItem
    ];

    private static readonly SidebarNavigationItem[] AuthorizedSidebarItems =
    [
        HomeSidebarItem,
        AccountAndVenueSidebarItem,
        GrabSidebarItem,
        GlobalLeakSidebarItem,
        TomorrowReservationSidebarItem,
        OccupySidebarItem,
        RemoteCheckInSidebarItem,
        MobileControlSidebarItem,
        NotificationSettingsSidebarItem,
        SettingsSidebarItem
    ];

    private Func<bool> _isAuthorized = static () => false;
    private Func<bool> _minimizeToTrayEnabled = static () => true;
    private bool _isSynchronizingSidebarSelection;

    public ObservableCollection<SidebarNavigationItem> SidebarItems { get; } =
    [
        HomeSidebarItem,
        AccountAndVenueSidebarItem,
        SettingsSidebarItem
    ];

    public const int AccountAndVenueTabIndex = 1;

    [ObservableProperty]
    private int selectedTabIndex;

    [ObservableProperty]
    private SidebarNavigationItem? selectedSidebarItem = HomeSidebarItem;

    [ObservableProperty]
    private int selectedNotificationSettingsTabIndex;

    public bool IsAccountAndVenuePageActive => SelectedTabIndex == AccountAndVenueTabIndex;

    public bool IsRemoteCheckInPageActive => SelectedTabIndex == RemoteCheckInTabIndex;

    public bool ShouldHideToTrayOnClose =>
        _minimizeToTrayEnabled() &&
        (IsTaskActive(grabSeatCoordinator.GetStatus()) ||
         IsTaskActive(globalLeakCoordinator.GetStatus()) ||
         IsTaskActive(occupySeatCoordinator.GetStatus()) ||
         IsTaskActive(tomorrowReservationCoordinator.GetStatus()));

    public void Configure(Func<bool> isAuthorized, Func<bool> minimizeToTrayEnabled)
    {
        _isAuthorized = isAuthorized;
        _minimizeToTrayEnabled = minimizeToTrayEnabled;
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        if (!_isAuthorized() && !IsTabAvailableWithoutAuthorization(value))
        {
            SelectedTabIndex = AccountAndVenueTabIndex;
            return;
        }

        SyncSelectedSidebarItem();
        OnPropertyChanged(nameof(IsAccountAndVenuePageActive));
        OnPropertyChanged(nameof(IsRemoteCheckInPageActive));
    }

    partial void OnSelectedSidebarItemChanged(SidebarNavigationItem? value)
    {
        if (_isSynchronizingSidebarSelection || value is null)
        {
            return;
        }

        if (SelectedTabIndex != value.PageIndex)
        {
            SelectedTabIndex = value.PageIndex;
        }
    }

    public static bool IsTabAvailableWithoutAuthorization(int tabIndex)
    {
        return tabIndex <= AccountAndVenueTabIndex || tabIndex == SystemSettingsTabIndex;
    }

    [RelayCommand]
    private void OpenHome()
    {
        SelectedTabIndex = 0;
    }

    [RelayCommand]
    private void OpenNotificationSettings()
    {
        SelectedTabIndex = NotificationSettingsTabIndex;
    }

    [RelayCommand]
    private void OpenSystemSettings()
    {
        SelectedTabIndex = SystemSettingsTabIndex;
    }

    [RelayCommand]
    private void ShowWindow()
    {
        appWindowService.ShowMainWindow();
    }

    [RelayCommand]
    private void QuitApplication()
    {
        appWindowService.QuitApplication();
    }

    public void UpdateSidebarItems()
    {
        var desiredItems = _isAuthorized() ? AuthorizedSidebarItems : UnauthorizedSidebarItems;
        if (SidebarItems.Count == desiredItems.Length &&
            SidebarItems.Select(item => item.PageIndex).SequenceEqual(desiredItems.Select(item => item.PageIndex)))
        {
            SyncSelectedSidebarItem();
            return;
        }

        SidebarItems.Clear();
        foreach (var item in desiredItems)
        {
            SidebarItems.Add(item);
        }

        SyncSelectedSidebarItem();
    }

    private void SyncSelectedSidebarItem()
    {
        var target = SidebarItems.FirstOrDefault(item => item.PageIndex == SelectedTabIndex)
            ?? SidebarItems.FirstOrDefault();

        _isSynchronizingSidebarSelection = true;
        try
        {
            SelectedSidebarItem = target;
        }
        finally
        {
            _isSynchronizingSidebarSelection = false;
        }
    }

    private static bool IsTaskActive(CoordinatorStatus status)
    {
        return status.State is CoordinatorTaskState.Starting
            or CoordinatorTaskState.Running
            or CoordinatorTaskState.Stopping;
    }
}
