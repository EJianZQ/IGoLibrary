using System.Buffers.Binary;
using System.Text.Json;

namespace IGoLibrary.Ex.Updater.Core;

public static class UpdatePipeProtocol
{
    private const int MaximumMessageBytes = 1024 * 1024;

    public static async Task WriteAsync<T>(
        Stream stream,
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, UpdateProtocol.JsonOptions);
        if (payload.Length <= 0 || payload.Length > MaximumMessageBytes)
        {
            throw new InvalidDataException("更新进程消息大小无效");
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken);
        await stream.WriteAsync(payload, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }

    public static async Task<T> ReadAsync<T>(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaximumMessageBytes)
        {
            throw new InvalidDataException("更新进程消息大小无效");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize<T>(payload, UpdateProtocol.JsonOptions)
               ?? throw new InvalidDataException("更新进程消息内容无效");
    }
}
