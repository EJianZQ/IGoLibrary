using Avalonia.Media;
using Avalonia.Media.Imaging;
using Net.Codecrete.QrCodeGenerator;

namespace IGoLibrary.Ex.Desktop.Services;

public sealed class QrCodeImageFactory : IQrCodeImageFactory
{
    public IImage Create(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        var qrCode = QrCode.EncodeText(text, QrCode.Ecc.Medium);
        var pngBytes = qrCode.ToPngBitmap(border: 4, scale: 8);
        return new Bitmap(new MemoryStream(pngBytes));
    }
}
