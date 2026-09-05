using IGoLibrary.Ex.Application.Updates;

namespace IGoLibrary.Ex.Tests;

public sealed class ReleaseNoteUpdatePolicyParserTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("### 更新内容\n\n- 修复问题")]
    public void Parse_WithoutMarker_IsUnrestrictedAndPreservesBody(string? body)
    {
        var result = ReleaseNoteUpdatePolicyParser.Parse(body, ParseVersion("1.0.6"));

        Assert.Same(AutomaticUpdatePolicy.Unrestricted, result.Policy);
        Assert.Equal(body ?? string.Empty, result.DisplayBody);
        Assert.Equal(ReleaseNoteUpdatePolicyIssue.None, result.Issue);
    }

    [Fact]
    public void Parse_ValidFirstContentLine_ExtractsMinimumAndPreservesOtherLineEndings()
    {
        const string body =
            "\r\n\t<!-- \tIGoLibrary-Ex-MinAutoUpdateVersion \t: \t1.0.5\t -->\r\n" +
            "### 更新内容\r\n\r\n- 修复问题\r\n";

        var result = ReleaseNoteUpdatePolicyParser.Parse(body, ParseVersion("1.0.6"));

        Assert.Equal(AutomaticUpdatePolicyKind.MinimumVersion, result.Policy.Kind);
        Assert.Equal(ParseVersion("1.0.5"), result.Policy.MinimumVersion);
        Assert.Equal("\r\n### 更新内容\r\n\r\n- 修复问题\r\n", result.DisplayBody);
        Assert.Equal(ReleaseNoteUpdatePolicyIssue.None, result.Issue);
    }

    [Fact]
    public void Evaluate_MinimumVersion_UsesInclusiveBoundary()
    {
        var policy = AutomaticUpdatePolicy.RequireMinimumVersion(ParseVersion("1.0.5"));

        Assert.Equal(
            AutomaticUpdateEligibility.CurrentVersionTooOld,
            policy.Evaluate(ParseVersion("1.0.4")));
        Assert.Equal(
            AutomaticUpdateEligibility.Supported,
            policy.Evaluate(ParseVersion("1.0.5")));
        Assert.Equal(
            AutomaticUpdateEligibility.Supported,
            policy.Evaluate(ParseVersion("1.1.0")));
    }

    [Theory]
    [InlineData("<!-- IGoLibrary-Ex-MinAutoUpdateVersion: v1.0.5 -->")]
    [InlineData("<!-- IGoLibrary-Ex-MinAutoUpdateVersion: 01.0.5 -->")]
    [InlineData("<!-- IGoLibrary-Ex-MinAutoUpdateVersion: 1.0.5-beta -->")]
    [InlineData("<!-- IGoLibrary-Ex-MinAutoUpdateVersion: 1.0 -->")]
    [InlineData("<!-- IGoLibrary-Ex-MinAutoUpdateVersion: 2147483648.0.0 -->")]
    [InlineData("<!-- IGoLibrary-Ex-MinAutoUpdateVersion: 1.0.5")]
    [InlineData("<!-- igolibrary-ex-minautoupdateversion: 1.0.5 -->")]
    [InlineData("正文 <!-- IGoLibrary-Ex-MinAutoUpdateVersion: 1.0.5 -->")]
    public void Parse_MalformedReservedMarker_FailsClosedAndRemovesReservedLine(string marker)
    {
        var result = ReleaseNoteUpdatePolicyParser.Parse(
            marker + "\n### 更新内容",
            ParseVersion("1.0.6"));

        Assert.Same(AutomaticUpdatePolicy.Invalid, result.Policy);
        Assert.Equal(
            AutomaticUpdateEligibility.InvalidReleasePolicy,
            result.Policy.Evaluate(ParseVersion("1.0.5")));
        Assert.Equal(ReleaseNoteUpdatePolicyIssue.InvalidMarkerSyntax, result.Issue);
        Assert.Equal("### 更新内容", result.DisplayBody);
    }

    [Fact]
    public void Parse_ValidMarkerAfterContent_FailsClosedAndRemovesMarkerLine()
    {
        const string body =
            "### 更新内容\n" +
            "<!-- IGoLibrary-Ex-MinAutoUpdateVersion: 1.0.5 -->\n" +
            "- 修复问题";

        var result = ReleaseNoteUpdatePolicyParser.Parse(body, ParseVersion("1.0.6"));

        Assert.Same(AutomaticUpdatePolicy.Invalid, result.Policy);
        Assert.Equal(ReleaseNoteUpdatePolicyIssue.MarkerNotFirstContentLine, result.Issue);
        Assert.Equal("### 更新内容\n- 修复问题", result.DisplayBody);
    }

    [Theory]
    [InlineData(
        "<!-- IGoLibrary-Ex-MinAutoUpdateVersion: 1.0.5 -->\n" +
        "<!-- IGoLibrary-Ex-MinAutoUpdateVersion: 1.0.5 -->")]
    [InlineData(
        "<!-- IGoLibrary-Ex-MinAutoUpdateVersion: 1.0.4 -->\n" +
        "<!-- IGoLibrary-Ex-MinAutoUpdateVersion: 1.0.5 -->")]
    public void Parse_DuplicateMarkers_FailClosedAndAreRemoved(string body)
    {
        var result = ReleaseNoteUpdatePolicyParser.Parse(body, ParseVersion("1.0.6"));

        Assert.Same(AutomaticUpdatePolicy.Invalid, result.Policy);
        Assert.Equal(ReleaseNoteUpdatePolicyIssue.DuplicateMarker, result.Issue);
        Assert.Equal(string.Empty, result.DisplayBody);
    }

    [Fact]
    public void Parse_MinimumAfterTarget_FailsClosed()
    {
        var result = ReleaseNoteUpdatePolicyParser.Parse(
            "<!-- IGoLibrary-Ex-MinAutoUpdateVersion: 1.0.7 -->",
            ParseVersion("1.0.6"));

        Assert.Same(AutomaticUpdatePolicy.Invalid, result.Policy);
        Assert.Equal(ReleaseNoteUpdatePolicyIssue.MinimumVersionAfterTarget, result.Issue);
    }

    [Fact]
    public void Parse_MinimumEqualToTarget_IsValidAndBlocksOlderVersions()
    {
        var result = ReleaseNoteUpdatePolicyParser.Parse(
            "<!-- IGoLibrary-Ex-MinAutoUpdateVersion: 1.0.6 -->",
            ParseVersion("1.0.6"));

        Assert.Equal(AutomaticUpdatePolicyKind.MinimumVersion, result.Policy.Kind);
        Assert.Equal(
            AutomaticUpdateEligibility.CurrentVersionTooOld,
            result.Policy.Evaluate(ParseVersion("1.0.5")));
    }

    [Fact]
    public void Parse_UnrelatedHtmlComment_IsPreserved()
    {
        const string body = "<!-- ordinary release comment -->\n### 更新内容";

        var result = ReleaseNoteUpdatePolicyParser.Parse(body, ParseVersion("1.0.6"));

        Assert.Same(AutomaticUpdatePolicy.Unrestricted, result.Policy);
        Assert.Equal(body, result.DisplayBody);
    }

    private static ReleaseVersion ParseVersion(string value)
    {
        Assert.True(ReleaseVersion.TryParse(value, out var version));
        return version;
    }
}
