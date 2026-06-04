using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.State;

public interface ISessionState
{
    SessionCredentials? Session { get; set; }
}

public interface IVenueState
{
    IReadOnlyList<LibrarySummary> Libraries { get; set; }

    LibrarySummary? BoundLibrary { get; set; }

    LibraryLayout? CurrentLayout { get; set; }
}

public interface IReservationState
{
    ReservationInfo? CurrentReservation { get; set; }
}

public sealed class AppRuntimeState : ISessionState, IVenueState, IReservationState
{
    public SessionCredentials? Session { get; set; }

    public IReadOnlyList<LibrarySummary> Libraries { get; set; } = [];

    public LibrarySummary? BoundLibrary { get; set; }

    public LibraryLayout? CurrentLayout { get; set; }

    public ReservationInfo? CurrentReservation { get; set; }
}
