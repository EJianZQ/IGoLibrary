namespace IGoLibrary.Ex.Application.Configuration;

public sealed record GlobalLeakTaskSettings
{
    /// <summary>
    /// 场馆按扫描优先级从高到低排列；集合中的第一项会在每轮最先扫描。
    /// </summary>
    public IReadOnlyList<GlobalLeakLibrarySelectionSettings> SelectedLibraries { get; init; } = [];

    public IReadOnlyList<GlobalLeakBlacklistedSeatSettings> BlacklistedSeats { get; init; } = [];

    public GlobalLeakTaskSettings()
    {
    }

    public GlobalLeakTaskSettings(IReadOnlyList<GlobalLeakLibrarySelectionSettings>? selectedLibraries)
    {
        SelectedLibraries = selectedLibraries ?? [];
    }

    public static GlobalLeakTaskSettings Default { get; } = new();
}

public sealed record GlobalLeakLibrarySelectionSettings(
    int LibraryId,
    string LibraryName,
    string Floor);
