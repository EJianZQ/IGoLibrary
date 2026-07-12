namespace IGoLibrary.Ex.Desktop.Services;

public sealed record SeatLabelDialogRequest(
    string Title,
    string Description,
    string? InitialText = null);

public interface ISeatLabelDialogService
{
    Task<string?> ShowAsync(
        SeatLabelDialogRequest request,
        CancellationToken cancellationToken = default);
}
