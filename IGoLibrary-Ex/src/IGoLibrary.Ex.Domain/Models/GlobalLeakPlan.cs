namespace IGoLibrary.Ex.Domain.Models;

public sealed record GlobalLeakPlan(
    IReadOnlyList<GlobalLeakLibraryTarget> Libraries,
    TimeSpan ScanInterval);
