using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.Services;

public interface IMobileControlOccupyPlanProvider
{
    Task<OccupySeatPlan> CreatePlanAsync(CancellationToken cancellationToken = default);
}
