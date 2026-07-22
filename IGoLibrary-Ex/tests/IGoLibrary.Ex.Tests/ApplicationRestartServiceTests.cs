using IGoLibrary.Ex.Desktop;
using IGoLibrary.Ex.Desktop.Services;

namespace IGoLibrary.Ex.Tests;

public sealed class ApplicationRestartServiceTests
{
    [Fact]
    public void RestartArguments_Parse_RemovesInternalParentOption()
    {
        var parsed = RestartArguments.Parse([
            "--user-option",
            "value",
            RestartArguments.ParentProcessIdOption,
            "1234"
        ]);

        Assert.Equal(1234, parsed.ParentProcessId);
        Assert.Equal(["--user-option", "value"], parsed.ApplicationArguments);
    }

    [Fact]
    public void RestartArguments_Parse_RemovesInternalUpdateOption()
    {
        var transactionId = Guid.NewGuid().ToString("N");

        var parsed = RestartArguments.Parse([
            "--user-option",
            RestartArguments.UpdateTransactionOption,
            transactionId
        ]);

        Assert.Equal(transactionId, parsed.UpdateTransactionId);
        Assert.Equal(["--user-option"], parsed.ApplicationArguments);
    }

    [Fact]
    public void RestartArguments_Parse_RemovesAndReturnsDataRestoreTransaction()
    {
        var transactionId = Guid.NewGuid().ToString("N");

        var parsed = RestartArguments.Parse([
            "--user-option",
            RestartArguments.RestoreTransactionOption,
            transactionId
        ]);

        Assert.Equal(transactionId, parsed.RestoreTransactionId);
        Assert.Equal(["--user-option"], parsed.ApplicationArguments);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void RestartArguments_Parse_RejectsInvalidDataRestoreTransaction(string value)
    {
        Assert.Throws<ArgumentException>(() => RestartArguments.Parse([
            RestartArguments.RestoreTransactionOption,
            value
        ]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void RestartArguments_Parse_RejectsInvalidUpdateTransaction(string value)
    {
        Assert.Throws<ArgumentException>(() => RestartArguments.Parse([
            RestartArguments.UpdateTransactionOption,
            value
        ]));
    }

    [Fact]
    public void BuildStartInfo_ForSelfContainedExecutable_DoesNotRepeatExecutablePath()
    {
        var executable = Path.GetFullPath("IGoLibrary.Ex.Desktop.exe");

        var info = ApplicationRestartService.BuildStartInfo(
            executable,
            [executable, "--user-option"],
            42);

        Assert.Equal(executable, info.FileName);
        Assert.Equal(
            ["--user-option", RestartArguments.ParentProcessIdOption, "42"],
            info.ArgumentList);
    }

    [Fact]
    public void BuildStartInfo_ForDotnetHost_PreservesManagedEntryAssembly()
    {
        var dotnet = Path.GetFullPath("dotnet.exe");
        var entryAssembly = Path.GetFullPath("IGoLibrary.Ex.Desktop.dll");

        var info = ApplicationRestartService.BuildStartInfo(
            dotnet,
            [entryAssembly, "--user-option"],
            42);

        Assert.Equal(
            [entryAssembly, "--user-option", RestartArguments.ParentProcessIdOption, "42"],
            info.ArgumentList);
    }
}
