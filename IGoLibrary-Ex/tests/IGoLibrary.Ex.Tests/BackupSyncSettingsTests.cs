using System.Text.Json;
using IGoLibrary.Ex.Application.Configuration;
using IGoLibrary.Ex.Infrastructure.Persistence;

namespace IGoLibrary.Ex.Tests;

public sealed class BackupSyncSettingsTests
{
    [Theory]
    [InlineData("https://dav.example.com/root", false, true)]
    [InlineData("http://dav.example.com/root", false, false)]
    [InlineData("http://dav.example.com/root", true, true)]
    [InlineData("https://user:pass@dav.example.com/", false, false)]
    [InlineData("https://dav.example.com/?secret=x", false, false)]
    [InlineData("file:///tmp/dav", false, false)]
    public void EndpointValidation_EnforcesTransportAndCredentialRules(
        string value,
        bool allowHttp,
        bool expected)
    {
        var valid = BackupSyncSettings.TryValidateEndpoint(
            value,
            allowHttp,
            out var uri,
            out _);

        Assert.Equal(expected, valid);
        Assert.Equal(expected, uri is not null);
    }

    [Theory]
    [InlineData("IGoLibrary-Ex", true)]
    [InlineData("备份/我的数据", true)]
    [InlineData("", true)]
    [InlineData("../backup", false)]
    [InlineData("folder/./backup", false)]
    public void RemoteDirectoryValidation_RejectsTraversal(string value, bool expected)
    {
        Assert.Equal(
            expected,
            BackupSyncSettings.TryValidateRemoteDirectory(value, out _, out _));
    }

    [Theory]
    [InlineData("IGoLibrary-Ex", "IGoLibrary-Ex/IGoLibrary-Ex.igobackup")]
    [InlineData("备份/我的数据", "备份/我的数据/IGoLibrary-Ex.igobackup")]
    [InlineData("", "IGoLibrary-Ex.igobackup")]
    public void BuildRemotePath_AppendsTheFixedFileName(string directory, string expected)
    {
        Assert.Equal(expected, BackupSyncSettings.BuildRemotePath(directory));
    }

    [Fact]
    public void MissingBackupSyncSettings_AddsSecureDefaults()
    {
        var migrated = SqliteSettingsRepository.MigrateAppSettingsJson("{}");
        using var document = JsonDocument.Parse(migrated);

        var backup = document.RootElement.GetProperty("backupSync");
        Assert.Equal(string.Empty, backup.GetProperty("endpoint").GetString());
        Assert.Equal(
            BackupSyncSettings.DefaultRemoteDirectory,
            backup.GetProperty("remoteDirectory").GetString());
        Assert.Equal(
            (int)WebDavTlsVerifyMode.Verify,
            backup.GetProperty("tlsVerifyMode").GetInt32());
        Assert.False(backup.GetProperty("allowInsecureHttp").GetBoolean());
        Assert.False(backup.GetProperty("autoUploadEnabled").GetBoolean());
    }
}
