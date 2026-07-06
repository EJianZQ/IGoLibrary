using System.Net;

namespace IGoLibrary.Ex.Desktop.Services;

public interface ILanAddressProvider
{
    IPAddress? GetPrimaryLanAddress();
}
