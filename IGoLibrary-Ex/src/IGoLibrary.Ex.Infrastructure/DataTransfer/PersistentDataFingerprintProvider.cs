using IGoLibrary.Ex.Application.Abstractions;
using IGoLibrary.Ex.Application.Backup;
using IGoLibrary.Ex.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace IGoLibrary.Ex.Infrastructure.DataTransfer;

internal sealed class PersistentDataFingerprintProvider(
    SqliteConnectionFactory connectionFactory,
    StorageLocations locations,
    ICredentialStore credentialStore,
    IBackupSecretStore backupSecretStore,
    ILogger<PersistentDataFingerprintProvider> logger) : IPersistentDataFingerprintProvider
{
    private readonly BackupWorkspaceManager _workspaceManager = new(locations);

    public async Task<string> ComputeAsync(CancellationToken cancellationToken = default)
    {
        var operationId = Guid.NewGuid().ToString("N");
        var workspace = _workspaceManager.Create("fingerprint", operationId);
        try
        {
            var snapshot = Path.Combine(workspace, "fingerprint.db");
            await using (var source = connectionFactory.Create())
            {
                await source.OpenAsync(cancellationToken);
                var builder = new SqliteConnectionStringBuilder
                {
                    DataSource = snapshot,
                    Mode = SqliteOpenMode.ReadWriteCreate,
                    Pooling = false
                };
                await using var target = new SqliteConnection(builder.ToString());
                await target.OpenAsync(cancellationToken);
                await Task.Run(() => source.BackupDatabase(target), cancellationToken);
            }

            var secrets = new BackupSecrets(
                await credentialStore.LoadSessionAsync(cancellationToken),
                await credentialStore.LoadRemoteCheckInSessionAsync(cancellationToken),
                await backupSecretStore.LoadWebDavPasswordAsync(cancellationToken));
            var inventory = await BackupInventoryReader.ReadAsync(snapshot, secrets, cancellationToken);
            return inventory.Fingerprint;
        }
        finally
        {
            try
            {
                _workspaceManager.Delete(workspace);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to clean semantic fingerprint workspace.");
            }
        }
    }
}
