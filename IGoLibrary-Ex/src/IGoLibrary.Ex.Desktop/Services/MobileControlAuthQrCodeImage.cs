using System.Reflection;

namespace IGoLibrary.Ex.Desktop.Services;

internal static class MobileControlAuthQrCodeImage
{
    private const string ResourceName = "IGoLibrary.Ex.Desktop.Assets.qrcode.png";
    private static readonly Lazy<byte[]> PngBytes = new(LoadPngBytes);

    public static ReadOnlyMemory<byte> GetPngBytes()
    {
        return PngBytes.Value;
    }

    private static byte[] LoadPngBytes()
    {
        var assembly = typeof(MobileControlAuthQrCodeImage).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"手机控制授权二维码资源不存在：{ResourceName}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
