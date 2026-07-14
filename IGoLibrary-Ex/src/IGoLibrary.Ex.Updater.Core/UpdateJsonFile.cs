using System.Text;
using System.Text.Json;

namespace IGoLibrary.Ex.Updater.Core;

public static class UpdateJsonFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public static T Read<T>(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var json = File.ReadAllText(path, Utf8NoBom);
        return JsonSerializer.Deserialize<T>(json, UpdateProtocol.JsonOptions)
               ?? throw new InvalidDataException($"JSON 文件内容为空或格式无效：{path}");
    }

    public static async Task<T> ReadAsync<T>(
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        await using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await JsonSerializer.DeserializeAsync<T>(
                   stream,
                   UpdateProtocol.JsonOptions,
                   cancellationToken)
               ?? throw new InvalidDataException($"JSON 文件内容为空或格式无效：{path}");
    }

    public static void WriteAtomic<T>(string path, T value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
                        ?? throw new InvalidOperationException($"无法确定 JSON 文件目录：{path}");
        Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var json = JsonSerializer.Serialize(value, UpdateProtocol.JsonOptions);
            File.WriteAllText(temporaryPath, json, Utf8NoBom);
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    public static async Task WriteAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var directory = Path.GetDirectoryName(Path.GetFullPath(path))
                        ?? throw new InvalidOperationException($"无法确定 JSON 文件目录：{path}");
        Directory.CreateDirectory(directory);

        var temporaryPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 16 * 1024,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    value,
                    UpdateProtocol.JsonOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporaryPath);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }
}
