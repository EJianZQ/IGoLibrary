using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Abstractions;

public interface ITraceIntApiClient
{
    Task<string> GetCookieFromCodeAsync(string code, CancellationToken cancellationToken = default);

    Task ValidateCookieAsync(string cookie, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<LibrarySummary>> GetLibrariesAsync(string cookie, CancellationToken cancellationToken = default);

    Task<LibraryLayout> GetLibraryLayoutAsync(string cookie, int libraryId, CancellationToken cancellationToken = default);

    Task<LibraryRule> GetLibraryRuleAsync(string cookie, int libraryId, CancellationToken cancellationToken = default);

    Task<ReservationInfo?> GetReservationInfoAsync(string cookie, CancellationToken cancellationToken = default);

    Task<bool> ReserveSeatAsync(string cookie, int libraryId, string seatKey, CancellationToken cancellationToken = default);

    Task<bool> CancelReservationAsync(string cookie, string reservationToken, CancellationToken cancellationToken = default);
}
