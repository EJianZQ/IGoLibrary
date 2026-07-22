using System.Security.Cryptography;
using System.Buffers.Binary;
using IGoLibrary.Ex.Infrastructure.DataTransfer;

namespace IGoLibrary.Ex.Tests;

public sealed class EncryptedBackupArchiveCodecTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "IGoLibrary-Ex-tests",
        Guid.NewGuid().ToString("N"));

    public EncryptedBackupArchiveCodecTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public async Task RoundTrip_PreservesAllEntries_WithUnicodePassword()
    {
        var manifest = "{\"formatVersion\":1}"u8.ToArray();
        var database = RandomNumberGenerator.GetBytes(EncryptedBackupArchiveCodec.ChunkSize + 73);
        var secrets = "{\"cookie\":\"sensitive\"}"u8.ToArray();
        var archive = await WriteArchiveAsync("图书馆🔐密码-123456", manifest, database, secrets);
        var output = Path.Combine(_directory, "roundtrip.db");

        var result = await new EncryptedBackupArchiveCodec().ReadAsync(
            archive,
            "图书馆🔐密码-123456",
            output);

        Assert.Equal(manifest, result.Manifest);
        Assert.Equal(secrets, result.Secrets);
        Assert.Equal(database, await File.ReadAllBytesAsync(output));
    }

    [Fact]
    public async Task WrongPassword_IsRejectedWithoutProducingUsableDatabase()
    {
        var archive = await WriteArchiveAsync(
            "correct-password",
            "{}"u8.ToArray(),
            "sqlite"u8.ToArray(),
            "{}"u8.ToArray());
        var output = Path.Combine(_directory, "wrong.db");

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EncryptedBackupArchiveCodec().ReadAsync(archive, "incorrect-password", output));

        Assert.Contains("密码错误或文件已损坏", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CiphertextTampering_IsRejected()
    {
        var archive = await WriteArchiveAsync(
            "tamper-password",
            "{}"u8.ToArray(),
            RandomNumberGenerator.GetBytes(4096),
            "{}"u8.ToArray());
        var bytes = await File.ReadAllBytesAsync(archive);
        bytes[^9] ^= 0x5A;
        var tampered = Path.Combine(_directory, "tampered.igobackup");
        await File.WriteAllBytesAsync(tampered, bytes);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EncryptedBackupArchiveCodec().ReadAsync(
                tampered,
                "tamper-password",
                Path.Combine(_directory, "tampered.db")));
    }

    [Fact]
    public async Task AuthenticatedHeaderTampering_IsRejected()
    {
        var archive = await WriteArchiveAsync(
            "header-password",
            "{}"u8.ToArray(),
            RandomNumberGenerator.GetBytes(1024),
            "{}"u8.ToArray());
        var bytes = await File.ReadAllBytesAsync(archive);
        bytes[16] ^= 0x01; // PBKDF2 iteration count remains valid, but the authenticated header changes.
        var tampered = Path.Combine(_directory, "header-tampered.igobackup");
        await File.WriteAllBytesAsync(tampered, bytes);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EncryptedBackupArchiveCodec().ReadAsync(
                tampered,
                "header-password",
                Path.Combine(_directory, "header-tampered.db")));
    }

    [Fact]
    public async Task DeclaredHashMismatch_IsRejectedAfterAuthenticatedDecryption()
    {
        var manifest = "{}"u8.ToArray();
        var database = "sqlite"u8.ToArray();
        var secrets = "{}"u8.ToArray();
        var path = Path.Combine(_directory, "wrong-hash.igobackup");
        var entries = new[]
        {
            Source(EncryptedBackupArchiveCodec.ManifestEntryName, manifest),
            new BackupArchiveSource(
                EncryptedBackupArchiveCodec.DatabaseEntryName,
                database.Length,
                new string('0', SHA256.HashSizeInBytes * 2),
                Content: database),
            Source(EncryptedBackupArchiveCodec.SecretsEntryName, secrets)
        };
        await new EncryptedBackupArchiveCodec().WriteAsync(path, "hash-password", entries);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EncryptedBackupArchiveCodec().ReadAsync(
                path,
                "hash-password",
                Path.Combine(_directory, "wrong-hash.db")));
    }

    [Fact]
    public async Task MetadataOverFourMiB_IsRejectedBeforeArchiveCreation()
    {
        var oversized = new byte[EncryptedBackupArchiveCodec.MaximumMetadataSize + 1];
        var entries = new[]
        {
            Source(EncryptedBackupArchiveCodec.ManifestEntryName, oversized),
            Source(EncryptedBackupArchiveCodec.DatabaseEntryName, "sqlite"u8.ToArray()),
            Source(EncryptedBackupArchiveCodec.SecretsEntryName, "{}"u8.ToArray())
        };
        var path = Path.Combine(_directory, "oversized.igobackup");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EncryptedBackupArchiveCodec().WriteAsync(path, "size-password", entries));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task TruncatedArchive_IsRejected()
    {
        var archive = await WriteArchiveAsync(
            "truncate-password",
            "{}"u8.ToArray(),
            RandomNumberGenerator.GetBytes(2048),
            "{}"u8.ToArray());
        var bytes = await File.ReadAllBytesAsync(archive);
        var truncated = Path.Combine(_directory, "truncated.igobackup");
        await File.WriteAllBytesAsync(truncated, bytes[..^21]);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EncryptedBackupArchiveCodec().ReadAsync(
                truncated,
                "truncate-password",
                Path.Combine(_directory, "truncated.db")));
    }

    [Fact]
    public async Task FutureFormatVersion_IsRejectedBeforeDecryption()
    {
        var archive = await WriteArchiveAsync(
            "version-password",
            "{}"u8.ToArray(),
            "sqlite"u8.ToArray(),
            "{}"u8.ToArray());
        var bytes = await File.ReadAllBytesAsync(archive);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12, 4), 2);
        var future = Path.Combine(_directory, "future.igobackup");
        await File.WriteAllBytesAsync(future, bytes);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EncryptedBackupArchiveCodec().ReadAsync(
                future,
                "version-password",
                Path.Combine(_directory, "future.db")));

        Assert.Contains("版本不受支持", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(28)]
    [InlineData(48)]
    public async Task UntrustedHeaderArrayLengths_AreRejectedBeforeAllocation(int fileOffset)
    {
        var archive = await WriteArchiveAsync(
            "length-password",
            "{}"u8.ToArray(),
            "sqlite"u8.ToArray(),
            "{}"u8.ToArray());
        var bytes = await File.ReadAllBytesAsync(archive);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(fileOffset, sizeof(int)), int.MaxValue);
        var malformed = Path.Combine(_directory, $"bad-length-{fileOffset}.igobackup");
        await File.WriteAllBytesAsync(malformed, bytes);

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EncryptedBackupArchiveCodec().ReadAsync(
                malformed,
                "length-password",
                Path.Combine(_directory, $"bad-length-{fileOffset}.db")));

        Assert.Contains("格式头无效", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UnknownOrDuplicateEntrySet_IsRejectedBeforeWriting()
    {
        var content = "{}"u8.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(content));
        var entries = new BackupArchiveSource[]
        {
            new(EncryptedBackupArchiveCodec.ManifestEntryName, content.Length, hash, Content: content),
            new(EncryptedBackupArchiveCodec.ManifestEntryName, content.Length, hash, Content: content),
            new("other.json", content.Length, hash, Content: content)
        };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new EncryptedBackupArchiveCodec().WriteAsync(
                Path.Combine(_directory, "invalid.igobackup"),
                "valid-password",
                entries));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private async Task<string> WriteArchiveAsync(
        string password,
        byte[] manifest,
        byte[] database,
        byte[] secrets)
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.igobackup");
        var entries = new[]
        {
            Source(EncryptedBackupArchiveCodec.ManifestEntryName, manifest),
            Source(EncryptedBackupArchiveCodec.DatabaseEntryName, database),
            Source(EncryptedBackupArchiveCodec.SecretsEntryName, secrets)
        };
        await new EncryptedBackupArchiveCodec().WriteAsync(path, password, entries);
        return path;
    }

    private static BackupArchiveSource Source(string name, byte[] content)
        => new(
            name,
            content.LongLength,
            Convert.ToHexString(SHA256.HashData(content)),
            Content: content);
}
