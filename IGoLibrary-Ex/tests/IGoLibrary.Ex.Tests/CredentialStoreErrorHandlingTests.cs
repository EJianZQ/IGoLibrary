using IGoLibrary.Ex.Infrastructure.Security;

namespace IGoLibrary.Ex.Tests;

public sealed class CredentialStoreErrorHandlingTests
{
    [Fact]
    public void WindowsDelete_TreatsMissingCredentialAsIdempotentSuccess()
    {
        WindowsCredentialStore.EnsureCredentialDeleted(deleted: false, errorCode: 1168);
        WindowsCredentialStore.EnsureCredentialDeleted(deleted: true, errorCode: 5);
    }

    [Fact]
    public void WindowsDelete_PropagatesRealCredentialManagerFailure()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            WindowsCredentialStore.EnsureCredentialDeleted(deleted: false, errorCode: 5));

        Assert.Contains("Win32 错误 5", exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void MacDelete_TreatsMissingKeychainItemAsIdempotentSuccess()
    {
        MacKeychainCredentialStore.EnsureSuccessfulExit(
            exitCode: 44,
            error: "item not found",
            tolerateItemNotFound: true);
    }

    [Fact]
    public void MacDelete_PropagatesRealKeychainFailure()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            MacKeychainCredentialStore.EnsureSuccessfulExit(
                exitCode: 1,
                error: "User interaction is not allowed.",
                tolerateItemNotFound: true));

        Assert.Equal("User interaction is not allowed.", exception.Message);
    }

    [Fact]
    public void MacWrite_DoesNotIgnoreItemNotFoundExitCode()
    {
        Assert.Throws<InvalidOperationException>(() =>
            MacKeychainCredentialStore.EnsureSuccessfulExit(
                exitCode: 44,
                error: "item not found",
                tolerateItemNotFound: false));
    }
}
