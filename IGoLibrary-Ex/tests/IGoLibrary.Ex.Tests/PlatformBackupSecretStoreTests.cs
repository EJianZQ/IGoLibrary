using System.Text;
using IGoLibrary.Ex.Infrastructure.Security;

namespace IGoLibrary.Ex.Tests;

public sealed class PlatformBackupSecretStoreTests
{
    [Fact]
    public void MacBackend_DecodesValuesWrittenByThePreviousCommandLineImplementation()
    {
        var encoded = "IGOB64:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("图书馆-password"));

        var result = MacBackupSecretBackend.DecodeStoredValue(encoded);

        Assert.Equal("图书馆-password", result);
    }

    [Fact]
    public void MacBackend_PreservesNativeKeychainValues()
    {
        Assert.Equal(
            "native-value",
            MacBackupSecretBackend.DecodeStoredValue("native-value"));
    }

    [Theory]
    [InlineData("normal-password")]
    [InlineData("IGOB64:looks-like-the-storage-prefix")]
    [InlineData("图书馆🔐密码")]
    public void MacBackend_EncodedStorageRoundTripsEveryPassword(string value)
    {
        Assert.Equal(
            value,
            MacBackupSecretBackend.DecodeStoredValue(
                MacBackupSecretBackend.EncodeStoredValue(value)));
    }
}
