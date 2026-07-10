using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using IGoLibrary.Ex.Domain.Models;

namespace IGoLibrary.Ex.Infrastructure.Api;

internal static class TraceIntRemoteCheckInPayloadEncoder
{
    internal const string PublicKeyPem =
        """
        -----BEGIN PUBLIC KEY-----
        MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEA0dmmkW4xPa+HhBTyaa0d
        gAb0fVZRS67jK4y15BQthjJ/ZuUZQmrbGqhG7rwnxfm7g+nFH9zEyRU5KLX3ty9j
        pNrPjyg7FBF9OvBDYHEt83b77W3mfBjpmoTJOt27E7RZ4InHqJQjqSEo4bw1PDz2
        OBmtlNIlXMu0VA8I0Bh39hBBnm0oouRV7FdqEzAp8nsF7a3VuBYpx9xek+cRVip0
        pMXI1AXM6bmyWWNzV0oikQW4ZIbutgDziTMeW28zl/hRbW9Ht34w0sWYyxumuLr1
        qweW3qnxycn3zn47weFYe6nJp71z+lgVtNTGtowNPPqBLXqusvwf+uNhSy1wKQFp
        UwIDAQAB
        -----END PUBLIC KEY-----
        """;

    public static string EncodeDevices(RemoteCheckInSignRequest request)
    {
        var payload = new object[][]
        {
            [request.BeaconUuid.ToUpperInvariant(), request.Major, request.Minor]
        };
        return EncodeJson(payload);
    }

    public static string EncodeLocation(RemoteCheckInSignRequest request)
    {
        return EncodeJson(new[] { request.Latitude, request.Longitude });
    }

    public static string EncryptTimestamp(string timestamp)
    {
        return EncryptTimestamp(timestamp, PublicKeyPem);
    }

    internal static string EncryptTimestamp(string timestamp, string publicKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);
        var plaintext = Encoding.UTF8.GetBytes(timestamp);
        var ciphertext = rsa.Encrypt(plaintext, RSAEncryptionPadding.Pkcs1);
        return Convert.ToBase64String(ciphertext);
    }

    private static string EncodeJson<T>(T value)
    {
        var json = JsonSerializer.Serialize(value);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }
}
