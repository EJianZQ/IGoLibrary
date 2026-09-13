using System.Text;
using IGoLibrary.Ex.Desktop.Services;
using Microsoft.AspNetCore.Http;

namespace IGoLibrary.Ex.Tests;

public sealed class NetworkLoggingMiddlewareTests
{
    [Theory]
    [InlineData(200)]
    [InlineData(401)]
    [InlineData(413)]
    public async Task CapturesOnlyConsumedRequestAndPreservesResponse(int status)
    {
        var logs = new NetworkLogTestContext();
        var context = new DefaultHttpContext();
        context.Request.Scheme = "http";
        context.Request.Host = new HostString("localhost");
        context.Request.Method = "POST";
        context.Request.ContentType = "application/json";
        context.Request.Body = new MemoryStream("{\"token\":\"private-secret\",\"seat\":\"A001\"}"u8.ToArray());
        context.Response.Body = new MemoryStream();
        var input = context.Request.Body;
        var output = context.Response.Body;
        await NetworkLoggingMiddleware.InvokeAsync(context, async ctx =>
        {
            if (status == 200)
            {
                using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
                Assert.Contains("private-secret", await reader.ReadToEndAsync());
            }
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"seat\":\"A001\"}");
        }, logs.Logger, "测试入站", "private-secret");
        Assert.Same(input, context.Request.Body);
        Assert.Same(output, context.Response.Body);
        Assert.Equal("{\"seat\":\"A001\"}", Encoding.UTF8.GetString(((MemoryStream)output).ToArray()));
        Assert.DoesNotContain("private-secret", logs.Writer.Text);
        Assert.Contains("A001", logs.Writer.Text);
        if (status != 200) { Assert.Equal(0, input.Position); Assert.Contains("未消费", logs.Writer.Text); }
    }

    [Fact]
    public async Task ExceptionRestoresStreamsAndRecordsIncompleteResponse()
    {
        var logs = new NetworkLogTestContext();
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream();
        context.Response.Body = new MemoryStream();
        var input = context.Request.Body;
        var output = context.Response.Body;
        var expected = new IOException("private-secret");
        var error = await Assert.ThrowsAsync<IOException>(() => NetworkLoggingMiddleware.InvokeAsync(context,
            _ => throw expected, logs.Logger, "测试", "private-secret"));
        Assert.Same(expected, error);
        Assert.Same(input, context.Request.Body);
        Assert.Same(output, context.Response.Body);
        Assert.DoesNotContain("private-secret", logs.Writer.Text);
        Assert.Contains("读取失败", logs.Writer.Text);
    }
}
