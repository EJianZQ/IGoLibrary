using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace IGoLibrary.Ex.Updater.Core;

public static class UpdatePipeProtocol
{
    private const int MaximumMessageBytes = 1024 * 1024;

    public static async Task WriteAsync<T>(
        Stream stream,
        T value,
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        var payload = JsonSerializer.SerializeToUtf8Bytes(value, jsonTypeInfo);
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
        JsonTypeInfo<T> jsonTypeInfo,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length <= 0 || length > MaximumMessageBytes)
        {
            throw new InvalidDataException("更新进程消息大小无效");
        }

        var payload = new byte[length];
        await stream.ReadExactlyAsync(payload, cancellationToken);
        return JsonSerializer.Deserialize(payload, jsonTypeInfo)
               ?? throw new InvalidDataException("更新进程消息内容无效");
    }
}
