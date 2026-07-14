using IGoLibrary.Ex.Updater.Core;

namespace IGoLibrary.Ex.Tests;

public sealed class UpdatePipeProtocolTests
{
    [Fact]
    public async Task RoundTrip_PreservesBootstrapResult()
    {
        var expected = new UpdateBootstrapResult(
            UpdateProtocol.SchemaVersion,
            Guid.NewGuid().ToString("N"),
            true,
            "ready",
            1234);
        await using var stream = new MemoryStream();

        await UpdatePipeProtocol.WriteAsync(stream, expected);
        stream.Position = 0;
        var actual = await UpdatePipeProtocol.ReadAsync<UpdateBootstrapResult>(stream);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task ReadAsync_RejectsOversizedFrameBeforeAllocatingPayload()
    {
        await using var stream = new MemoryStream();
        await stream.WriteAsync(BitConverter.GetBytes(1024 * 1024 + 1));
        stream.Position = 0;

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            UpdatePipeProtocol.ReadAsync<UpdateBootstrapResult>(stream));
    }
}
