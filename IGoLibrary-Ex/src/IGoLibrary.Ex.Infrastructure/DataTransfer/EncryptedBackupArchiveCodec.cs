using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using IGoLibrary.Ex.Application.Backup;

namespace IGoLibrary.Ex.Infrastructure.DataTransfer;

internal sealed record BackupArchiveSource(
    string Name,
    long Length,
    string Sha256,
    string? FilePath = null,
    byte[]? Content = null);

internal sealed record BackupArchiveContents(
    byte[] Manifest,
    byte[] Secrets);

internal sealed class EncryptedBackupArchiveCodec
{
    public const int FormatVersion = 1;
    public const int DatabaseSchemaVersion = 1;
    public const int ChunkSize = 1024 * 1024;
    public const int Pbkdf2Iterations = 600_000;
    public const long MaximumArchiveSize = 2L * 1024 * 1024 * 1024;
    public const int MaximumMetadataSize = 4 * 1024 * 1024;

    public const string ManifestEntryName = "manifest.json";
    public const string DatabaseEntryName = "data.db";
    public const string SecretsEntryName = "secrets.json";

    private const int KeySize = 32;
    private const int SaltSize = 16;
    private const int NoncePrefixSize = 8;
    private const int TagSize = 16;
    private const int EndMarker = 0x21444E45;
    private static readonly byte[] Magic = "IGOBKP01"u8.ToArray();
    private static readonly HashSet<string> AllowedEntryNames =
    [
        ManifestEntryName,
        DatabaseEntryName,
        SecretsEntryName
    ];

    public async Task WriteAsync(
        string outputPath,
        string password,
        IReadOnlyList<BackupArchiveSource> entries,
        CancellationToken cancellationToken = default)
    {
        BackupPasswordRules.Validate(password);
        ValidateSources(entries);

        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixSize);
        var header = BuildHeader(salt, noncePrefix, entries.Count);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            passwordBytes,
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        try
        {
            await using var output = new FileStream(
                outputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                ChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var writer = new BinaryWriter(output, Encoding.UTF8, leaveOpen: true);
            writer.Write(header.Length);
            writer.Write(header);

            using var aes = new AesGcm(key, TagSize);
            var plainBuffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
            var cipherBuffer = ArrayPool<byte>.Shared.Rent(ChunkSize);
            var tag = new byte[TagSize];
            var nonce = new byte[12];
            var globalChunkIndex = 0u;
            try
            {
                for (var entryIndex = 0; entryIndex < entries.Count; entryIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var entry = entries[entryIndex];
                    var nameBytes = Encoding.UTF8.GetBytes(entry.Name);
                    var hashBytes = Convert.FromHexString(entry.Sha256);
                    writer.Write(nameBytes.Length);
                    writer.Write(nameBytes);
                    writer.Write(entry.Length);
                    writer.Write(hashBytes.Length);
                    writer.Write(hashBytes);

                    var chunkCount = Math.Max(1L, (entry.Length + ChunkSize - 1) / ChunkSize);
                    if (chunkCount > uint.MaxValue || globalChunkIndex > uint.MaxValue - chunkCount)
                    {
                        throw new InvalidDataException("备份内容分块数量超出格式限制");
                    }

                    writer.Write((int)chunkCount);
                    await using var source = OpenSource(entry);
                    var remaining = entry.Length;
                    for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
                    {
                        var expectedLength = (int)Math.Min(ChunkSize, remaining);
                        await ReadExactlyAsync(source, plainBuffer.AsMemory(0, expectedLength), cancellationToken);
                        BuildNonce(noncePrefix, globalChunkIndex, nonce);
                        var aad = BuildAdditionalData(
                            header,
                            entryIndex,
                            nameBytes,
                            entry.Length,
                            hashBytes,
                            chunkIndex,
                            chunkIndex == chunkCount - 1);
                        aes.Encrypt(
                            nonce,
                            plainBuffer.AsSpan(0, expectedLength),
                            cipherBuffer.AsSpan(0, expectedLength),
                            tag,
                            aad);
                        writer.Write(expectedLength);
                        writer.Write(tag);
                        await output.WriteAsync(
                            cipherBuffer.AsMemory(0, expectedLength),
                            cancellationToken);
                        remaining -= expectedLength;
                        globalChunkIndex++;
                    }

                    if (source.Position != entry.Length)
                    {
                        throw new InvalidDataException($"备份条目长度在读取期间发生变化：{entry.Name}");
                    }
                }

                writer.Write(EndMarker);
                writer.Write(globalChunkIndex);
                BuildNonce(noncePrefix, globalChunkIndex, nonce);
                var endAad = BuildEndAdditionalData(header, globalChunkIndex);
                aes.Encrypt(nonce, ReadOnlySpan<byte>.Empty, Span<byte>.Empty, tag, endAad);
                writer.Write(tag);
                await output.FlushAsync(cancellationToken);
                if (output.Length > MaximumArchiveSize)
                {
                    throw new InvalidDataException("备份文件超过 2 GiB 限制");
                }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainBuffer);
                CryptographicOperations.ZeroMemory(cipherBuffer);
                CryptographicOperations.ZeroMemory(tag);
                CryptographicOperations.ZeroMemory(nonce);
                ArrayPool<byte>.Shared.Return(plainBuffer);
                ArrayPool<byte>.Shared.Return(cipherBuffer);
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(salt);
            CryptographicOperations.ZeroMemory(noncePrefix);
        }
    }

    public async Task<BackupArchiveContents> ReadAsync(
        string inputPath,
        string password,
        string databaseOutputPath,
        CancellationToken cancellationToken = default)
    {
        BackupPasswordRules.Validate(password);
        var fileInfo = new FileInfo(inputPath);
        if (!fileInfo.Exists || fileInfo.Length <= 0 || fileInfo.Length > MaximumArchiveSize)
        {
            throw new InvalidDataException("备份文件不存在、为空或超过 2 GiB 限制");
        }

        await using var input = new FileStream(
            inputPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            ChunkSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        var headerLength = reader.ReadInt32();
        if (headerLength is < 40 or > 1024)
        {
            throw new InvalidDataException("备份格式头长度无效");
        }

        var header = reader.ReadBytes(headerLength);
        if (header.Length != headerLength)
        {
            throw new InvalidDataException("备份格式头不完整");
        }

        var parsedHeader = ParseHeader(header);
        var passwordBytes = Encoding.UTF8.GetBytes(password);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            passwordBytes,
            parsedHeader.Salt,
            parsedHeader.Iterations,
            HashAlgorithmName.SHA256,
            KeySize);
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var globalChunkIndex = 0u;
        var nonce = new byte[12];
        try
        {
            using var aes = new AesGcm(key, TagSize);
            for (var entryIndex = 0; entryIndex < parsedHeader.EntryCount; entryIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var nameLength = reader.ReadInt32();
                if (nameLength is <= 0 or > 128)
                {
                    throw new InvalidDataException("备份条目名称长度无效");
                }

                var nameBytes = reader.ReadBytes(nameLength);
                if (nameBytes.Length != nameLength)
                {
                    throw new InvalidDataException("备份条目名称不完整");
                }

                var name = Encoding.UTF8.GetString(nameBytes);
                if (!AllowedEntryNames.Contains(name) || !seen.Add(name))
                {
                    throw new InvalidDataException($"备份包含未知或重复条目：{name}");
                }

                var length = reader.ReadInt64();
                var metadataLimit = name == DatabaseEntryName ? MaximumArchiveSize : MaximumMetadataSize;
                if (length < 0 || length > metadataLimit)
                {
                    throw new InvalidDataException($"备份条目长度无效：{name}");
                }

                var hashLength = reader.ReadInt32();
                if (hashLength != SHA256.HashSizeInBytes)
                {
                    throw new InvalidDataException("备份条目哈希长度无效");
                }

                var expectedHash = reader.ReadBytes(hashLength);
                if (expectedHash.Length != hashLength)
                {
                    throw new InvalidDataException("备份条目哈希不完整");
                }

                var expectedChunkCount = Math.Max(1L, (length + parsedHeader.ChunkSize - 1) / parsedHeader.ChunkSize);
                var chunkCount = reader.ReadInt32();
                if (chunkCount != expectedChunkCount)
                {
                    throw new InvalidDataException("备份条目分块数量无效");
                }

                await using var destination = CreateDestination(name, length, databaseOutputPath, out var memory);
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var remaining = length;
                for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
                {
                    var expectedLength = (int)Math.Min(parsedHeader.ChunkSize, remaining);
                    var cipherLength = reader.ReadInt32();
                    if (cipherLength != expectedLength)
                    {
                        throw new InvalidDataException("备份密文分块长度无效");
                    }

                    var tag = reader.ReadBytes(TagSize);
                    var cipher = reader.ReadBytes(cipherLength);
                    if (tag.Length != TagSize || cipher.Length != cipherLength)
                    {
                        throw new InvalidDataException("备份密文分块不完整");
                    }

                    var plain = ArrayPool<byte>.Shared.Rent(Math.Max(1, cipherLength));
                    try
                    {
                        BuildNonce(parsedHeader.NoncePrefix, globalChunkIndex, nonce);
                        var aad = BuildAdditionalData(
                            header,
                            entryIndex,
                            nameBytes,
                            length,
                            expectedHash,
                            chunkIndex,
                            chunkIndex == chunkCount - 1);
                        aes.Decrypt(nonce, cipher, tag, plain.AsSpan(0, cipherLength), aad);
                        await destination.WriteAsync(plain.AsMemory(0, cipherLength), cancellationToken);
                        hasher.AppendData(plain.AsSpan(0, cipherLength));
                    }
                    catch (CryptographicException ex)
                    {
                        throw new InvalidDataException("备份密码错误或文件已损坏", ex);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(plain);
                        CryptographicOperations.ZeroMemory(cipher);
                        CryptographicOperations.ZeroMemory(tag);
                        ArrayPool<byte>.Shared.Return(plain);
                    }

                    remaining -= cipherLength;
                    globalChunkIndex++;
                }

                await destination.FlushAsync(cancellationToken);
                var actualHash = hasher.GetHashAndReset();
                if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash))
                {
                    throw new InvalidDataException($"备份条目哈希校验失败：{name}");
                }

                if (memory is not null)
                {
                    result.Add(name, memory.ToArray());
                }
            }

            var endMarker = reader.ReadInt32();
            var authenticatedChunkCount = reader.ReadUInt32();
            var endTag = reader.ReadBytes(TagSize);
            if (seen.Count != AllowedEntryNames.Count ||
                endMarker != EndMarker ||
                authenticatedChunkCount != globalChunkIndex ||
                endTag.Length != TagSize ||
                input.Position != input.Length)
            {
                throw new InvalidDataException("备份文件结尾、条目集合或长度无效");
            }

            try
            {
                BuildNonce(parsedHeader.NoncePrefix, globalChunkIndex, nonce);
                aes.Decrypt(
                    nonce,
                    ReadOnlySpan<byte>.Empty,
                    endTag,
                    Span<byte>.Empty,
                    BuildEndAdditionalData(header, globalChunkIndex));
            }
            catch (CryptographicException ex)
            {
                throw new InvalidDataException("备份结束信息认证失败，文件可能已被截断或篡改", ex);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(endTag);
            }

            return new BackupArchiveContents(
                result[ManifestEntryName],
                result[SecretsEntryName]);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("备份文件已被截断", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(parsedHeader.Salt);
            CryptographicOperations.ZeroMemory(parsedHeader.NoncePrefix);
            CryptographicOperations.ZeroMemory(nonce);
        }
    }

    private static Stream CreateDestination(
        string name,
        long length,
        string databaseOutputPath,
        out MemoryStream? memory)
    {
        if (name == DatabaseEntryName)
        {
            memory = null;
            return new FileStream(
                databaseOutputPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                ChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }

        memory = new ZeroingMemoryStream((int)length);
        return memory;
    }

    private static void ValidateSources(IReadOnlyList<BackupArchiveSource> entries)
    {
        if (entries.Count != AllowedEntryNames.Count ||
            entries.Select(entry => entry.Name).ToHashSet(StringComparer.Ordinal).Count != entries.Count ||
            entries.Any(entry => !AllowedEntryNames.Contains(entry.Name)))
        {
            throw new InvalidDataException("备份条目集合无效");
        }

        foreach (var entry in entries)
        {
            var hasFile = !string.IsNullOrWhiteSpace(entry.FilePath);
            var hasContent = entry.Content is not null;
            var validSource = hasFile ^ hasContent;
            var fileExists = hasFile && File.Exists(entry.FilePath);
            var sourceLength = fileExists ? new FileInfo(entry.FilePath!).Length : entry.Content?.LongLength ?? -1;
            if (!validSource || hasFile && !fileExists || sourceLength != entry.Length || entry.Length < 0 ||
                entry.Length > (entry.Name == DatabaseEntryName ? MaximumArchiveSize : MaximumMetadataSize) ||
                entry.Sha256.Length != SHA256.HashSizeInBytes * 2)
            {
                throw new InvalidDataException($"备份条目来源无效：{entry.Name}");
            }
        }

        long estimatedLength = 4 + 1024 + 4 + 4 + TagSize;
        foreach (var entry in entries)
        {
            var chunkCount = Math.Max(1L, (entry.Length + ChunkSize - 1) / ChunkSize);
            estimatedLength = checked(
                estimatedLength +
                4 + Encoding.UTF8.GetByteCount(entry.Name) + 8 + 4 + SHA256.HashSizeInBytes + 4 +
                entry.Length + chunkCount * (4 + TagSize));
        }

        if (estimatedLength > MaximumArchiveSize)
        {
            throw new InvalidDataException("备份文件预计会超过 2 GiB 限制");
        }
    }

    private static Stream OpenSource(BackupArchiveSource entry)
    {
        if (entry.Content is not null)
        {
            return new MemoryStream(entry.Content, writable: false);
        }

        return new FileStream(
            entry.FilePath!,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            ChunkSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
    }

    private static byte[] BuildHeader(byte[] salt, byte[] noncePrefix, int entryCount)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(Pbkdf2Iterations);
        writer.Write(ChunkSize);
        writer.Write(entryCount);
        writer.Write(salt.Length);
        writer.Write(salt);
        writer.Write(noncePrefix.Length);
        writer.Write(noncePrefix);
        writer.Flush();
        return stream.ToArray();
    }

    private static ParsedHeader ParseHeader(byte[] header)
    {
        try
        {
            using var stream = new MemoryStream(header, writable: false);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
            var magic = reader.ReadBytes(Magic.Length);
            var version = reader.ReadInt32();
            var iterations = reader.ReadInt32();
            var chunkSize = reader.ReadInt32();
            var entryCount = reader.ReadInt32();
            var saltLength = reader.ReadInt32();
            if (!magic.SequenceEqual(Magic) ||
                version != FormatVersion ||
                iterations is < 100_000 or > 10_000_000 ||
                chunkSize != ChunkSize ||
                entryCount != AllowedEntryNames.Count ||
                saltLength != SaltSize ||
                stream.Length - stream.Position < SaltSize + sizeof(int))
            {
                throw new InvalidDataException("备份格式头无效或版本不受支持");
            }

            var salt = reader.ReadBytes(SaltSize);
            var noncePrefixLength = reader.ReadInt32();
            if (noncePrefixLength != NoncePrefixSize ||
                stream.Length - stream.Position != NoncePrefixSize)
            {
                CryptographicOperations.ZeroMemory(salt);
                throw new InvalidDataException("备份格式头无效或版本不受支持");
            }

            var noncePrefix = reader.ReadBytes(NoncePrefixSize);
            return new ParsedHeader(iterations, chunkSize, entryCount, salt, noncePrefix);
        }
        catch (EndOfStreamException ex)
        {
            throw new InvalidDataException("备份格式头无效或版本不受支持", ex);
        }

    }

    private static byte[] BuildAdditionalData(
        byte[] header,
        int entryIndex,
        byte[] name,
        long length,
        byte[] hash,
        long chunkIndex,
        bool isLast)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(header);
        Span<byte> numbers = stackalloc byte[21];
        BinaryPrimitives.WriteInt32LittleEndian(numbers, entryIndex);
        BinaryPrimitives.WriteInt64LittleEndian(numbers[4..], length);
        BinaryPrimitives.WriteInt64LittleEndian(numbers[12..], chunkIndex);
        numbers[20] = isLast ? (byte)1 : (byte)0;
        hasher.AppendData(numbers);
        hasher.AppendData(name);
        hasher.AppendData(hash);
        return hasher.GetHashAndReset();
    }

    private static byte[] BuildEndAdditionalData(byte[] header, uint globalChunkCount)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hasher.AppendData(header);
        Span<byte> ending = stackalloc byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(ending, EndMarker);
        BinaryPrimitives.WriteUInt32LittleEndian(ending[4..], globalChunkCount);
        hasher.AppendData(ending);
        return hasher.GetHashAndReset();
    }

    private static void BuildNonce(ReadOnlySpan<byte> prefix, uint index, Span<byte> nonce)
    {
        prefix.CopyTo(nonce);
        BinaryPrimitives.WriteUInt32BigEndian(nonce[NoncePrefixSize..], index);
    }

    private static async Task ReadExactlyAsync(
        Stream source,
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await source.ReadAsync(buffer[offset..], cancellationToken);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
        }
    }

    private sealed record ParsedHeader(
        int Iterations,
        int ChunkSize,
        int EntryCount,
        byte[] Salt,
        byte[] NoncePrefix);

    private sealed class ZeroingMemoryStream(int capacity) : MemoryStream(capacity)
    {
        protected override void Dispose(bool disposing)
        {
            if (TryGetBuffer(out var buffer) && buffer.Array is not null)
            {
                CryptographicOperations.ZeroMemory(buffer.Array.AsSpan());
            }

            base.Dispose(disposing);
        }
    }

}
