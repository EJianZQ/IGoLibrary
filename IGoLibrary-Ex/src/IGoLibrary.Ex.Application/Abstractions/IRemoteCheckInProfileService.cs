using IGoLibrary.Ex.Application.Configuration;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface IRemoteCheckInProfileService
{
    Task<RemoteCheckInVenueProfileSettings?> GetForLibraryAsync(
        int libraryId,
        CancellationToken cancellationToken = default);

    Task<RemoteCheckInVenueProfileSettings> SaveAsync(
        RemoteCheckInVenueProfileSettings profile,
        CancellationToken cancellationToken = default);
}
