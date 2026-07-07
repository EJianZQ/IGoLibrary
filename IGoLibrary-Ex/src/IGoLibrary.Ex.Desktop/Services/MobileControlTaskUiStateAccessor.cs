using IGoLibrary.Ex.Desktop.ViewModels;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class MobileControlTaskUiStateAccessor(
    TomorrowReservationPageViewModel tomorrowReservationPage) : IMobileControlTaskUiStateAccessor
{
    public TimeSpan? TomorrowScheduledStartTime => tomorrowReservationPage.TomorrowScheduledStartTime;

    public string TomorrowVerificationText => tomorrowReservationPage.TomorrowVerificationText;
}
