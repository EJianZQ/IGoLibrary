using CommunityToolkit.Mvvm.ComponentModel;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Desktop.ViewModels;

public sealed partial class GlobalLeakLibraryPriorityItemViewModel(
    GlobalLeakLibraryTarget target) : ViewModelBase
{
    public GlobalLeakLibraryTarget Target { get; } = target;

    public int LibraryId => Target.LibraryId;

    public string LibraryName => Target.LibraryName;

    public string Floor => Target.Floor;

    [ObservableProperty]
    private int priority;

    [ObservableProperty]
    private bool canMoveUp;

    [ObservableProperty]
    private bool canMoveDown;

    [ObservableProperty]
    private bool isDropBefore;

    [ObservableProperty]
    private bool isDropAfter;

    internal void UpdatePosition(int index, int count)
    {
        Priority = index + 1;
        CanMoveUp = index > 0;
        CanMoveDown = index < count - 1;
    }
}
