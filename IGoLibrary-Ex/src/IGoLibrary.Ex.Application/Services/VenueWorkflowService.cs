using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Application.Services;

public sealed class VenueWorkflowService(
    ILibraryService libraryService,
    ISessionService sessionService,
    ITraceIntApiClient apiClient,
    ISettingsService settingsService) : IVenueWorkflowService
{
    public async Task<VenueLibraryLoadResult> LoadLibrariesAsync(
        bool restorePreferredSelection,
        int? preferredLibraryId = null,
        CancellationToken cancellationToken = default)
    {
        var libraries = await libraryService.LoadLibrariesAsync(cancellationToken);

        LibrarySummary? selected = null;
        if (preferredLibraryId is not null)
        {
            selected = libraries.FirstOrDefault(x => x.LibraryId == preferredLibraryId.Value);
        }
        else if (restorePreferredSelection)
        {
            var settings = await settingsService.LoadAsync(cancellationToken);
            selected = libraries.FirstOrDefault(x => x.LibraryId == settings.Venue.LastLibraryId)
                ?? libraries.FirstOrDefault();
        }

        return new VenueLibraryLoadResult(libraries, selected);
    }

    public async Task<VenueBindingResult> BindLibraryAsync(
        int libraryId,
        CancellationToken cancellationToken = default)
    {
        var layout = await libraryService.BindLibraryAsync(libraryId, cancellationToken);
        var ruleResult = await TryLoadLibraryRuleAsync(libraryId, cancellationToken);
        var favorites = await libraryService.GetFavoritesAsync(libraryId, cancellationToken);
        return new VenueBindingResult(layout, ruleResult.Rule, favorites, ruleResult.FailureMessage);
    }

    public async Task<VenueBindingResult> RefreshBoundLibraryAsync(CancellationToken cancellationToken = default)
    {
        var layout = await libraryService.RefreshBoundLibraryAsync(cancellationToken);
        var favorites = await libraryService.GetFavoritesAsync(layout.LibraryId, cancellationToken);
        return new VenueBindingResult(layout, null, favorites);
    }

    public async Task<VenuePreviewResult> PreviewLibraryAsync(
        LibrarySummary library,
        CancellationToken cancellationToken = default)
    {
        var session = sessionService.CurrentSession ?? throw new InvalidOperationException("当前未登录。");
        var layout = await apiClient.GetLibraryLayoutAsync(session.Cookie, library.LibraryId, cancellationToken);
        var ruleResult = await TryLoadLibraryRuleAsync(library.LibraryId, cancellationToken);
        return new VenuePreviewResult(layout, ruleResult.Rule, ruleResult.FailureMessage);
    }

    public async Task<LibraryRule?> LoadLibraryRuleAsync(
        int libraryId,
        CancellationToken cancellationToken = default)
    {
        var session = sessionService.CurrentSession;
        if (session is null)
        {
            return null;
        }

        return await apiClient.GetLibraryRuleAsync(session.Cookie, libraryId, cancellationToken);
    }

    private async Task<LibraryRuleLoadResult> TryLoadLibraryRuleAsync(
        int libraryId,
        CancellationToken cancellationToken)
    {
        try
        {
            return new LibraryRuleLoadResult(
                await LoadLibraryRuleAsync(libraryId, cancellationToken),
                FailureMessage: null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new LibraryRuleLoadResult(null, ex.Message);
        }
    }

    public Task<IReadOnlyList<SeatReference>> GetFavoritesAsync(
        int libraryId,
        CancellationToken cancellationToken = default)
    {
        return libraryService.GetFavoritesAsync(libraryId, cancellationToken);
    }

    public Task SaveFavoritesAsync(
        int libraryId,
        IReadOnlyList<SeatReference> seats,
        CancellationToken cancellationToken = default)
    {
        return libraryService.SaveFavoritesAsync(libraryId, seats, cancellationToken);
    }

    private sealed record LibraryRuleLoadResult(
        LibraryRule? Rule,
        string? FailureMessage);
}
