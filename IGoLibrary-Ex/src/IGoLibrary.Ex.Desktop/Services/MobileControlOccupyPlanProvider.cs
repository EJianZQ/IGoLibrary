using Avalonia.Threading;
using IGoLibrary.Ex.Desktop.ViewModels;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Helpers;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class MobileControlOccupyPlanProvider(
    OccupyPageViewModel occupyPage) : IMobileControlOccupyPlanProvider
{
    public async Task<OccupySeatPlan> CreatePlanAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await Dispatcher.UIThread.InvokeAsync(() => OccupySeatPlanFactory.Create(
            occupyPage.ReReserveDelaySeconds,
            (OccupyCheckIntervalMode)occupyPage.SelectedOccupyCheckIntervalModeIndex));
    }
}
