using Avalonia.Controls.ApplicationLifetimes;
using IGoLibrary.Ex.Desktop;

namespace IGoLibrary.Ex.Tests;

public sealed class AppActivationTests
{
    [Fact]
    public void ShouldRestoreMainWindowForActivation_ReturnsTrueForDockReopen()
    {
        Assert.True(App.ShouldRestoreMainWindowForActivation(ActivationKind.Reopen));
    }

    [Theory]
    [InlineData(ActivationKind.Background)]
    [InlineData(ActivationKind.File)]
    [InlineData(ActivationKind.OpenUri)]
    public void ShouldRestoreMainWindowForActivation_ReturnsFalseForNonReopenActivations(ActivationKind kind)
    {
        Assert.False(App.ShouldRestoreMainWindowForActivation(kind));
    }
}
