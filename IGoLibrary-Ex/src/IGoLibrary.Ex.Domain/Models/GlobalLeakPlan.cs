namespace IGoLibrary.Ex.Domain.Models;

/// <param name="Libraries">场馆按扫描优先级从高到低排列。</param>
public sealed record GlobalLeakPlan(
    IReadOnlyList<GlobalLeakLibraryTarget> Libraries,
    TimeSpan ScanInterval);
