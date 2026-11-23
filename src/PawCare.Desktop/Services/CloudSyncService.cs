using System.Data;
using System.IO;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PawCare.Desktop.Models;

namespace PawCare.Desktop.Services;

public sealed record SyncResult(int EventCount, string Destination, DateTime CompletedUtc);
public sealed record CloudBackupResult(string LocalDestination, string CloudDestination, DateTime CompletedUtc);

public sealed class CloudSyncService
{
    private readonly DatabasePaths _paths;
    private readonly ClinicDataStore _store;
    private readonly string _ownerId = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";

    public CloudSyncService(DatabasePaths paths, ClinicDataStore store)
    {
        _paths = paths;
        _store = store;
    }

    public string LocalBackupTarget => Path.Combine(_paths.LocalBackupDirectory, _store.TenantId, _store.BranchId);
    public string CloudBackupTarget => Path.Combine(_paths.CloudMirrorDirectory, _store.TenantId, _store.BranchId, "full-backups");

    public SyncStatus GetStatus()
    {
        using var connection = Open();
        var incremental = ReadMetadata(connection, "LastIncrementalSyncUtc");
        var backup = ReadMetadata(connection, "LastFullBackupUtc");
        return new SyncStatus(
            _store.PendingSyncEvents(),
            DateTime.TryParse(incremental, out var incrementalDate) ? incrementalDate.ToUniversalTime() : null,
            DateTime.TryParse(backup, out var backupDate) ? backupDate.ToUniversalTime() : null,
            "SQL Server local-first · cloud mirror available");
    }

    public SyncResult SyncIncremental()
    {
        _store.Save();
        AcquireLease();
        try
        {
            using var connection = Open();
            var events = new List<Dictionary<string, object?>>();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT Sequence,EventId,IdempotencyKey,EntityType,EntityId,Operation,Version,Payload,OccurredUtc FROM dbo.OutboxEvents WHERE SyncState='Pending' ORDER BY Sequence;";
                using var reader = command.ExecuteReader();
                while (reader.Read())
                {
                    events.Add(new Dictionary<string, object?>
                    {
                        ["sequence"] = reader.GetInt64(0), ["eventId"] = reader.GetString(1), ["idempotencyKey"] = reader.GetString(2),
                        ["entityType"] = reader.GetString(3), ["entityId"] = reader.GetString(4), ["operation"] = reader.GetString(5),
                        ["version"] = reader.GetInt64(6), ["payload"] = JsonSerializer.Deserialize<JsonElement>(reader.GetString(7)),
                        ["occurredUtc"] = DateTime.SpecifyKind(reader.GetDateTime(8), DateTimeKind.Utc)
                    });
                }
            }

            var completed = DateTime.UtcNow;
            var directory = Path.Combine(_paths.CloudMirrorDirectory, _store.TenantId, _store.BranchId, "incremental");
            Directory.CreateDirectory(directory);
            var destination = Path.Combine(directory, $"{completed:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.json");
            var temporary = destination + ".tmp";
            var envelope = new
            {
                schemaVersion = 2,
                tenantId = _store.TenantId,
                branchId = _store.BranchId,
                sourceDatabase = _store.DatabaseName,
                sourceDevice = _store.DeviceId,
                createdUtc = completed,
                mode = "incremental",
                events
            };
            File.WriteAllText(temporary, JsonSerializer.Serialize(envelope, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, destination, true);

            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            foreach (var item in events)
            {
                using var update = connection.CreateCommand();
                update.Transaction = transaction;
                update.CommandText = "UPDATE dbo.OutboxEvents SET SyncState='Synced',SyncedUtc=@time WHERE EventId=@id AND SyncState='Pending';";
                update.Parameters.AddWithValue("@time", completed);
                update.Parameters.AddWithValue("@id", Convert.ToString(item["eventId"])!);
                update.ExecuteNonQuery();
            }
            WriteMetadata(connection, transaction, "LastIncrementalSyncUtc", completed.ToString("O"));
            transaction.Commit();
            return new SyncResult(events.Count, destination, completed);
        }
        finally
        {
            ReleaseLease();
        }
    }

    public CloudBackupResult CreateCloudBackup()
    {
        _store.Save();
        AcquireLease();
        try
        {
            var completed = DateTime.UtcNow;
            Directory.CreateDirectory(LocalBackupTarget);
            Directory.CreateDirectory(CloudBackupTarget);
            var fileName = $"{_store.DatabaseName}-{completed:yyyyMMdd-HHmmssfff}.bak";
            var localDestination = Path.Combine(LocalBackupTarget, fileName);
            var cloudDestination = Path.Combine(CloudBackupTarget, fileName);

            using var connection = Open();
            if (!_store.DatabaseName.All(x => char.IsLetterOrDigit(x) || x == '_'))
                throw new InvalidOperationException("The tenant database name is not safe for backup.");
            using (var backup = connection.CreateCommand())
            {
                backup.CommandTimeout = 180;
                backup.CommandText = $"BACKUP DATABASE [{_store.DatabaseName}] TO DISK=@path WITH COPY_ONLY, INIT, CHECKSUM;";
                backup.Parameters.AddWithValue("@path", localDestination);
                backup.ExecuteNonQuery();
            }

            var temporaryCloudCopy = cloudDestination + ".uploading";
            try
            {
                File.Copy(localDestination, temporaryCloudCopy, true);
                File.Move(temporaryCloudCopy, cloudDestination, true);
            }
            finally
            {
                if (File.Exists(temporaryCloudCopy)) File.Delete(temporaryCloudCopy);
            }

            using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
            WriteMetadata(connection, transaction, "LastFullBackupUtc", completed.ToString("O"));
            transaction.Commit();
            return new CloudBackupResult(localDestination, cloudDestination, completed);
        }
        finally
        {
            ReleaseLease();
        }
    }

    public SyncResult CreateFullBackup()
    {
        var result = CreateCloudBackup();
        return new SyncResult(1, result.CloudDestination, result.CompletedUtc);
    }

    private void AcquireLease()
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        string? owner = null;
        DateTime? expires = null;
        using (var read = connection.CreateCommand())
        {
            read.Transaction = transaction;
            read.CommandText = "SELECT OwnerId,ExpiresUtc FROM dbo.SyncLease WITH (UPDLOCK,HOLDLOCK) WHERE Id=1;";
            using var reader = read.ExecuteReader();
            if (reader.Read())
            {
                owner = reader.GetString(0);
                expires = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc);
            }
        }
        if (owner is not null && owner != _ownerId && expires > DateTime.UtcNow)
            throw new DataConcurrencyException("Another sync or backup is already running for this branch. Wait for it to finish and try again.");

        using var write = connection.CreateCommand();
        write.Transaction = transaction;
        write.CommandText = "UPDATE dbo.SyncLease SET OwnerId=@owner,ExpiresUtc=@expires WHERE Id=1; IF @@ROWCOUNT=0 INSERT INTO dbo.SyncLease(Id,OwnerId,ExpiresUtc) VALUES(1,@owner,@expires);";
        write.Parameters.AddWithValue("@owner", _ownerId);
        write.Parameters.AddWithValue("@expires", DateTime.UtcNow.AddMinutes(5));
        write.ExecuteNonQuery();
        transaction.Commit();
    }

    private void ReleaseLease()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM dbo.SyncLease WHERE Id=1 AND OwnerId=@owner;";
        command.Parameters.AddWithValue("@owner", _ownerId);
        command.ExecuteNonQuery();
    }

    private SqlConnection Open()
    {
        var connection = new SqlConnection(_store.ConnectionString);
        connection.Open();
        return connection;
    }

    private static string? ReadMetadata(SqlConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Value FROM dbo.SyncMetadata WHERE [Key]=@key;";
        command.Parameters.AddWithValue("@key", key);
        return Convert.ToString(command.ExecuteScalar());
    }

    private static void WriteMetadata(SqlConnection connection, SqlTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE dbo.SyncMetadata WITH (UPDLOCK) SET Value=@value WHERE [Key]=@key; IF @@ROWCOUNT=0 INSERT INTO dbo.SyncMetadata([Key],Value) VALUES(@key,@value);";
        command.Parameters.AddWithValue("@key", key);
        command.Parameters.AddWithValue("@value", value);
        command.ExecuteNonQuery();
    }
}
