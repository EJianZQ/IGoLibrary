using IGoLibrary.Ex.Application.Updates;

namespace IGoLibrary.Ex.Tests;

public sealed class ReleaseVersionTests
{
    [Theory]
    [InlineData("v0.3-beta", "0.3.0-beta", true)]
    [InlineData("v0.4.0-beta.1", "0.4.0-beta.1", true)]
    [InlineData("v0.4.0-rc.1", "0.4.0-rc.1", true)]
    [InlineData("v1.0.0", "1.0.0", false)]
    public void TryParse_NormalizesSupportedReleaseTags(
        string input,
        string expected,
        bool expectedPrerelease)
    {
        var parsed = ReleaseVersion.TryParse(input, out var version);

        Assert.True(parsed);
        Assert.Equal(expected, version.ToString());
        Assert.Equal(expectedPrerelease, version.IsPrerelease);
    }

    [Theory]
    [InlineData("Public1.3")]
    [InlineData("vnext")]
    [InlineData("1")]
    public void TryParse_RejectsUnsupportedReleaseTags(string input)
    {
        Assert.False(ReleaseVersion.TryParse(input, out _));
    }

    [Fact]
    public void CompareTo_TreatsStableAsNewerThanPrerelease_ForSameCoreVersion()
    {
        Assert.True(Parse("1.0.0") > Parse("1.0.0-rc.1"));
        Assert.True(Parse("1.0.0-rc.1") > Parse("1.0.0-beta.2"));
        Assert.True(Parse("1.0.0-beta.2") > Parse("1.0.0-beta.1"));
    }

    private static ReleaseVersion Parse(string value)
    {
        Assert.True(ReleaseVersion.TryParse(value, out var version));
        return version;
    }
}
