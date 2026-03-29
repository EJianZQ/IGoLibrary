using Avalonia;

namespace IGoLibrary.Ex.Desktop.Services;

internal static class ToastLayoutCalculator
{
    public static IReadOnlyList<PixelPoint> Calculate(
        PixelRect workingArea,
        IReadOnlyList<PixelSize> toastSizes,
        int margin,
        int spacing)
    {
        if (toastSizes.Count == 0)
        {
            return [];
        }

        var positions = new PixelPoint[toastSizes.Count];
        var bottomCursor = workingArea.Bottom - margin;

        for (var index = toastSizes.Count - 1; index >= 0; index--)
        {
            var size = toastSizes[index];
            bottomCursor -= size.Height;
            var x = workingArea.Right - margin - size.Width;
            positions[index] = new PixelPoint(x, bottomCursor);
            bottomCursor -= spacing;
        }

        return positions;
    }
}
