using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Configuration;

namespace IGoLibrary.Ex.Application.Services;

public sealed class RemoteCheckInProfileService(ISettingsService settingsService) : IRemoteCheckInProfileService
{
    public async Task<RemoteCheckInVenueProfileSettings?> GetForLibraryAsync(
        int libraryId,
        CancellationToken cancellationToken = default)
    {
        var settings = await settingsService.LoadAsync(cancellationToken);
        return (settings.RemoteCheckIn ?? RemoteCheckInSettings.Default).VenueProfiles
            .FirstOrDefault(profile => profile.LibraryId == libraryId);
    }

    public async Task<RemoteCheckInVenueProfileSettings> SaveAsync(
        RemoteCheckInVenueProfileSettings profile,
        CancellationToken cancellationToken = default)
    {
        var normalized = RemoteCheckInProfileValidator.NormalizeAndValidate(profile);
        await settingsService.UpdateAsync(current =>
        {
            var remoteCheckIn = current.RemoteCheckIn ?? RemoteCheckInSettings.Default;
            var profiles = remoteCheckIn.VenueProfiles
                .Where(existing => existing.LibraryId != normalized.LibraryId)
                .Append(normalized)
                .OrderBy(existing => existing.LibraryId)
                .ToArray();

            return current with
            {
                RemoteCheckIn = remoteCheckIn with { VenueProfiles = profiles }
            };
        }, cancellationToken);

        return normalized;
    }
}
