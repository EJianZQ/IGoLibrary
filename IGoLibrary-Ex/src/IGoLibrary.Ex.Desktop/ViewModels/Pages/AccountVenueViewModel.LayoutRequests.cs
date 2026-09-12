namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class AccountVenueViewModel
{
    private CancellationTokenSource? _layoutRequest;
    private CancellationTokenSource? _bindingLayoutRequest;
    private long _layoutRequestVersion;

    private bool CanRefreshSeats() => _bindingLayoutRequest is null;

    private (CancellationTokenSource Request, long Version) BeginLayoutRequest(bool binding = false)
    {
        _layoutRequest?.Cancel();
        _layoutRequest = new CancellationTokenSource();
        if (binding) SetBindingLayoutRequest(_layoutRequest);
        return (_layoutRequest, ++_layoutRequestVersion);
    }
    private bool IsCurrentLayoutRequest(long version, CancellationToken token) =>
        version == _layoutRequestVersion && !token.IsCancellationRequested;

    private void FinishLayoutRequest(CancellationTokenSource request)
    {
        if (ReferenceEquals(request, _layoutRequest)) _layoutRequest = null;
        if (ReferenceEquals(request, _bindingLayoutRequest)) SetBindingLayoutRequest(null);
        request.Dispose();
    }
    private void CancelLayoutRequest()
    {
        ++_layoutRequestVersion;
        _layoutRequest?.Cancel();
        _layoutRequest = null;
        SetBindingLayoutRequest(null);
    }

    private void SetBindingLayoutRequest(CancellationTokenSource? request)
    {
        _bindingLayoutRequest = request;
        RefreshSeatsCommand.NotifyCanExecuteChanged();
    }
}
