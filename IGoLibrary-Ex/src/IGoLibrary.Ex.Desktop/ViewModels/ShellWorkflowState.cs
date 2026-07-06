using CommunityToolkit.Mvvm.ComponentModel;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class ShellWorkflowState : ViewModelBase
{
    [ObservableProperty]
    private bool isInitializationComplete;

    [ObservableProperty]
    private bool isAuthorized;

    [ObservableProperty]
    private string currentCookie = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? currentCookieExpirationTime;

    [ObservableProperty]
    private LibrarySummary? selectedLibrary;

    [ObservableProperty]
    private LibrarySummary? lockedLibrary;

    [ObservableProperty]
    private LibraryLayout? currentLayout;

    [ObservableProperty]
    private ReservationInfo? currentReservation;
}
