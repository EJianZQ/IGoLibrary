using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.State;
using IGoLibrary.Ex.Domain.Enums;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class LibraryService(
    ITraceIntApiClient apiClient,
    IFavoritesRepository favoritesRepository,
    ISettingsService settingsService,
    IActivityLogService activityLogService,
    ISessionState sessionState,
    IVenueState venueState) : ILibraryService
{
    public LibrarySummary? BoundLibrary => venueState.BoundLibrary;

    public async Task<IReadOnlyList<LibrarySummary>> LoadLibrariesAsync(CancellationToken cancellationToken = default)
    {
        var cookie = sessionState.Session?.Cookie ?? throw new InvalidOperationException("当前未登录。");
        var libraries = await apiClient.GetLibrariesAsync(cookie, cancellationToken);
        venueState.Libraries = libraries;
        activityLogService.Write(LogEntryKind.Success, "Library", $"已获取 {libraries.Count} 个可绑定场馆。");
        return libraries;
    }

    public async Task<LibraryLayout> BindLibraryAsync(int libraryId, CancellationToken cancellationToken = default)
    {
        var cookie = sessionState.Session?.Cookie ?? throw new InvalidOperationException("当前未登录。");
        var libraries = venueState.Libraries.Count > 0
            ? venueState.Libraries
            : await apiClient.GetLibrariesAsync(cookie, cancellationToken);

        var target = libraries.FirstOrDefault(x => x.LibraryId == libraryId)
            ?? throw new InvalidOperationException("未找到指定场馆。");
        var layout = await apiClient.GetLibraryLayoutAsync(cookie, libraryId, cancellationToken);

        venueState.Libraries = libraries;
        venueState.BoundLibrary = target;
        venueState.CurrentLayout = layout;

        await settingsService.UpdateAsync(settings => settings with
        {
            Venue = new VenueSelectionSettings(target.LibraryId, target.Name)
        }, cancellationToken);

        activityLogService.Write(LogEntryKind.Success, "Library", $"已绑定场馆：{target.Name}。");
        return layout;
    }

    public async Task<LibraryLayout> RefreshBoundLibraryAsync(CancellationToken cancellationToken = default)
    {
        var cookie = sessionState.Session?.Cookie ?? throw new InvalidOperationException("当前未登录。");
        var library = venueState.BoundLibrary ?? throw new InvalidOperationException("当前未绑定场馆。");
        var layout = await apiClient.GetLibraryLayoutAsync(cookie, library.LibraryId, cancellationToken);
        venueState.CurrentLayout = layout;
        return layout;
    }

    public Task<IReadOnlyList<SeatReference>> GetFavoritesAsync(int libraryId, CancellationToken cancellationToken = default)
    {
        return favoritesRepository.GetFavoritesAsync(libraryId, cancellationToken);
    }

    public async Task SaveFavoritesAsync(int libraryId, IReadOnlyList<SeatReference> seats, CancellationToken cancellationToken = default)
    {
        await favoritesRepository.SaveFavoritesAsync(libraryId, seats, cancellationToken);
        activityLogService.Write(LogEntryKind.Success, "Favorite", $"已保存 {seats.Count} 个收藏座位。");
    }
}
