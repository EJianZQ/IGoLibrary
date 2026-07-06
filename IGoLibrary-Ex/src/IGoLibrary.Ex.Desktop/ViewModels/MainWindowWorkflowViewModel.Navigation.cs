using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public partial class MainWindowWorkflowViewModel
{
    private bool _navigationConfigured;

    public ObservableCollection<SidebarNavigationItem> SidebarItems => Navigation.SidebarItems;

    public const int AccountAndVenueTabIndex = ShellNavigationViewModel.AccountAndVenueTabIndex;

    public int SelectedTabIndex
    {
        get => Navigation.SelectedTabIndex;
        set
        {
            EnsureNavigationConfigured();
            Navigation.SelectedTabIndex = value;
        }
    }

    public SidebarNavigationItem? SelectedSidebarItem
    {
        get => Navigation.SelectedSidebarItem;
        set
        {
            EnsureNavigationConfigured();
            Navigation.SelectedSidebarItem = value;
        }
    }

    public bool IsAccountAndVenuePageActive => Navigation.IsAccountAndVenuePageActive;

    public int SelectedNotificationSettingsTabIndex
    {
        get => Navigation.SelectedNotificationSettingsTabIndex;
        set => Navigation.SelectedNotificationSettingsTabIndex = value;
    }

    public bool ShouldHideToTrayOnClose => Navigation.ShouldHideToTrayOnClose;

    public IRelayCommand OpenHomeCommand => Navigation.OpenHomeCommand;

    public IRelayCommand OpenNotificationSettingsCommand => Navigation.OpenNotificationSettingsCommand;

    public IRelayCommand OpenSystemSettingsCommand => Navigation.OpenSystemSettingsCommand;

    public IRelayCommand ShowWindowCommand => Navigation.ShowWindowCommand;

    public IRelayCommand QuitApplicationCommand => Navigation.QuitApplicationCommand;

    private void ConfigureNavigationPropertyBridge(ViewModelPropertyBridge propertyBridge)
    {
        propertyBridge.Forward(
            Navigation,
            nameof(ShellNavigationViewModel.SelectedTabIndex),
            nameof(SelectedTabIndex),
            nameof(IsAccountAndVenuePageActive));
        propertyBridge.Forward(
            Navigation,
            nameof(ShellNavigationViewModel.IsAccountAndVenuePageActive),
            nameof(IsAccountAndVenuePageActive));
        propertyBridge.ForwardSame(
            Navigation,
            nameof(SelectedSidebarItem),
            nameof(SelectedNotificationSettingsTabIndex));
    }

    private void EnsureNavigationConfigured()
    {
        if (_navigationConfigured)
        {
            return;
        }

        Navigation.Configure(() => IsAuthorized, () => MinimizeToTrayEnabled);
        _navigationConfigured = true;
    }

    private static bool IsTabAvailableWithoutAuthorization(int tabIndex)
    {
        return ShellNavigationViewModel.IsTabAvailableWithoutAuthorization(tabIndex);
    }

    private void UpdateSidebarItems()
    {
        EnsureNavigationConfigured();
        Navigation.UpdateSidebarItems();
    }
}
