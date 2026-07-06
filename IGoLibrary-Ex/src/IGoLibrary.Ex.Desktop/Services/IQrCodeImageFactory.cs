using Avalonia.Media;

namespace IGoLibrary.Ex.Desktop.Services;

public interface IQrCodeImageFactory
{
    IImage Create(string text);
}
