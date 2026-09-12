namespace IGoLibrary.Ex.Desktop.Services;

/// <summary>根据官方布局样例确认的设施类型；仅用于展示，不决定座位资格。</summary>
internal static class SeatFacilityCatalog
{
    public static (string AssetName, string DisplayName)? Get(int type) => type switch
    {
        2 => ("desk", "桌子"),
        3 => ("entrance", "入口"),
        6 => ("pillar", "柱子"),
        7 => ("window", "窗"),
        // type=8 有名称时展示原始文字（含“柱”和方向），空名称时展示书架。
        8 => ("bookrack", "书架"),
        _ => null
    };
}
