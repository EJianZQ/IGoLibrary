using System.Reflection;

namespace IGoLibrary.Ex.Desktop.Services;

internal static class AuthQrCodeImageResource
{
    private const string ResourceName = "IGoLibrary.Ex.Desktop.Assets.qrcode.png";
    private static readonly Lazy<byte[]> PngBytes = new(LoadPngBytes);

    public static ReadOnlyMemory<byte> GetPngBytes()
    {
        return PngBytes.Value;
    }

    private static byte[] LoadPngBytes()
    {
        var assembly = typeof(AuthQrCodeImageResource).GetTypeInfo().Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"授权二维码资源不存在：{ResourceName}");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
