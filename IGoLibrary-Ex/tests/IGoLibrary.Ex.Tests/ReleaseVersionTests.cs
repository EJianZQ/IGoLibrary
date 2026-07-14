using IGoLibrary.Ex.Application.Updates;

namespace IGoLibrary.Ex.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("v1.0.0", "1.0.0")]
    [InlineData("2.10.3", "2.10.3")]
    public void TryParse_NormalizesStableReleaseTags(string input, string expected)
    {
        var parsed = ReleaseVersion.TryParse(input, out var version);

        Assert.True(parsed);
        Assert.Equal(expected, version.ToString());
    }

    [Theory]
    [InlineData("Public1.3")]
    [InlineData("vnext")]
    [InlineData("1")]
    [InlineData("1.0")]
    [InlineData("1.0.0-beta")]
    [InlineData("1.0.0-rc.1")]
    [InlineData("1.0.０")]
    [InlineData("01.0.0")]
    [InlineData("1.00.0")]
    [InlineData("1.0.00")]
    public void TryParse_RejectsUnsupportedReleaseTags(string input)
    {
        Assert.False(ReleaseVersion.TryParse(input, out _));
    }

    [Fact]
    public void CompareTo_OrdersStableVersionsByThreeComponents()
    {
        Assert.True(Parse("1.0.1") > Parse("1.0.0"));
        Assert.True(Parse("1.1.0") > Parse("1.0.99"));
        Assert.True(Parse("2.0.0") > Parse("1.99.99"));
    }

    private static ReleaseVersion Parse(string value)
    {
        Assert.True(ReleaseVersion.TryParse(value, out var version));
        return version;
    }
}
