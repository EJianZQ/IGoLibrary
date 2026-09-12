using Avalonia.Threading;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class SeatWorkspaceViewModel
{
    private bool _preferredListMode;
    public string[] ViewModes { get; } = ["场馆布局视图", "列表视图"];
    public int SelectedViewIndex
    {
        get => IsListMode ? 1 : 0;
        set
        {
            if (value is not (0 or 1) || value == SelectedViewIndex) return;
            _preferredListMode = value == 1;
            if (_viewPreferences is not null) _viewPreferences.Select(_preferredListMode);
            else ApplyViewPreference();
            OnPropertyChanged(nameof(SelectedViewIndex));
        }
    }

    public Task InitializeViewPreferenceAsync() => _viewPreferences?.InitializeAsync() ?? Task.CompletedTask;
    public Task FlushViewPreferenceAsync() => _viewPreferences?.FlushAsync() ?? Task.CompletedTask;
    private void OnViewPreferenceChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.UIThread.CheckAccess()) ApplyViewPreference();
        else Dispatcher.UIThread.Post(ApplyViewPreference);
    }
    private void ApplyViewPreference()
    {
        if (_disposed) return;
        _preferredListMode = _viewPreferences?.PreferList ?? _preferredListMode;
        IsListMode = _preferredListMode || SourceLayout is not null && !Projection.CanShowMap;
    }
}
