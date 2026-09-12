namespace IGoLibrary.Ex.Domain.Models;

/// <summary>场馆中的原始空间元素；类型值保留服务端定义，不决定预约资格。</summary>
public sealed record LibraryLayoutItem(
    int Type, int X, int Y, string Key, string Name, int? SeatStatus, bool? IsOccupied);
