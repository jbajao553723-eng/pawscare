using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using PawCare.Desktop.Models;

namespace PawCare.Desktop.Services;

public sealed class ClinicDataStore
{
    private sealed record StoredEntity(string Payload, long Version, bool IsDeleted);

    private static readonly string[] EntityTables =
    [
        "Clients", "Appointments", "Inquiries", "Complaints", "Feedback", "Reminders",
        "Promotions", "Consultations", "Inventory", "Activities"
    ];

    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly string _connectionString;
    private readonly string _masterConnectionString;
    private long _loadedRevision;
    private string _actorId;
    private string _actorName;
    private UserRole? _actorRole;
    private SubscriptionFeatures _subscriptionFeatures = SubscriptionFeatures.All;

    public ClinicData Data { get; private set; } = new();
    public string StoragePath { get; }
    public string DatabaseName { get; }
    public string ConnectionString => _connectionString;
    public string TenantId { get; }
    public string BranchId { get; }
    public string DeviceId { get; }

    public ClinicDataStore(string? databaseName = null, string tenantId = "tenant-a", string actorId = "system", string actorName = "System", DatabasePaths? paths = null, string? branchId = null)
    {
        paths ??= new DatabasePaths();
        TenantId = tenantId;
        BranchId = branchId ?? tenantId + "-main";
        DatabaseName = databaseName ?? paths.TenantDatabaseName(tenantId);
        StoragePath = $"{paths.ServerName} / {DatabaseName}";
        DeviceId = Environment.MachineName + "-" + Environment.UserName;
        _actorId = actorId;
        _actorName = actorName;
        _connectionString = paths.DatabaseConnectionString(DatabaseName);
        _masterConnectionString = paths.MasterConnectionString;
    }

    public void SetActor(UserSession session)
    {
        _actorId = session.UserId;
        _actorName = session.Name;
        _actorRole = session.Role;
    }

    public void Load()
    {
        using var connection = Open();
        InitializeSchema(connection);
        _loadedRevision = ReadRevision(connection);
        Data = new ClinicData
        {
            Clients = LoadCollection<ClientRecord>(connection, "Clients"),
            Appointments = LoadCollection<AppointmentRecord>(connection, "Appointments"),
            Inquiries = LoadCollection<InquiryRecord>(connection, "Inquiries"),
            Reminders = LoadCollection<ReminderRecord>(connection, "Reminders"),
            Promotions = LoadCollection<PromotionRecord>(connection, "Promotions"),
            Consultations = LoadCollection<ConsultationRecord>(connection, "Consultations"),
            Inventory = LoadCollection<InventoryRecord>(connection, "Inventory"),
            Activities = LoadCollection<ActivityRecord>(connection, "Activities")
        };
        var cases = LoadCollection<FeedbackRecord>(connection, "Complaints")
            .Concat(LoadCollection<FeedbackRecord>(connection, "Feedback"))
            .OrderByDescending(x => x.Date)
            .ToArray();
        var normalizedLegacyComplaints = false;
        foreach (var record in cases.Where(x => x.CaseType != "Complaint" && !string.IsNullOrWhiteSpace(x.Escalation) && x.Escalation != "None"))
        {
            record.CaseType = "Complaint";
            normalizedLegacyComplaints = true;
        }
        Data.Feedback = new ObservableCollection<FeedbackRecord>(cases);

        if (_loadedRevision != 0 || Data.Clients.Count != 0)
        {
            if (normalizedLegacyComplaints) Save();
            return;
        }
        Data = TryLoadLegacyData() ?? (BranchId == TenantId + "-main" && (TenantId is "tenant-a" or "tenant-b" or "tenant-c") ? Seed() : new ClinicData());
        Save();
    }

    public void Save()
    {
        EnsureActorActive();
        ValidatePromotionCodes();
        using var connection = Open();
        InitializeSchema(connection);
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        var currentRevision = ReadRevision(connection, transaction, true);
        if (currentRevision != _loadedRevision)
            throw new DataConcurrencyException("This branch database changed in another session. PawCare reloaded the latest records to prevent lost updates.");

        var changed = false;
        changed |= SaveCollection(connection, transaction, "Clients", Data.Clients, x => x.Id);
        changed |= SaveCollection(connection, transaction, "Appointments", Data.Appointments, x => x.Id);
        changed |= SaveCollection(connection, transaction, "Inquiries", Data.Inquiries, x => x.Id);
        changed |= SaveCollection(connection, transaction, "Complaints", Data.Feedback.Where(x => x.CaseType == "Complaint"), x => x.Id);
        changed |= SaveCollection(connection, transaction, "Feedback", Data.Feedback.Where(x => x.CaseType != "Complaint"), x => x.Id);
        changed |= SaveCollection(connection, transaction, "Reminders", Data.Reminders, x => x.Id);
        changed |= SaveCollection(connection, transaction, "Promotions", Data.Promotions, x => x.Id);
        changed |= SaveCollection(connection, transaction, "Consultations", Data.Consultations, x => x.Id);
        changed |= SaveCollection(connection, transaction, "Inventory", Data.Inventory, x => x.Id);
        changed |= SaveCollection(connection, transaction, "Activities", Data.Activities, x => x.Id);

        if (changed)
        {
            _loadedRevision = currentRevision + 1;
            WriteMetadata(connection, transaction, "DataRevision", _loadedRevision.ToString());
            WriteMetadata(connection, transaction, "LastLocalWriteUtc", DateTime.UtcNow.ToString("O"));
        }
        transaction.Commit();
    }

    public void Reload() => Load();

    public void Reset()
    {
        if (_actorRole is not null and not UserRole.Admin)
            throw new UnauthorizedAccessException("Only the branch administrator can reset demo records.");
        Data = Seed();
        Save();
    }

    private void EnsureActorActive()
    {
        if (_actorRole is null) return;
        using var connection = new SqlConnection(_masterConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) COALESCE(p.Features,4095) FROM dbo.Users u
            JOIN dbo.UserTenantRoles r ON r.UserId=u.Id
            LEFT JOIN dbo.Tenants t ON t.Id=r.TenantId
            LEFT JOIN dbo.Branches b ON b.Id=@branch AND b.TenantId=t.Id
            LEFT JOIN dbo.SubscriptionPlans p ON p.Code=t.SubscriptionPlanCode
            WHERE u.Id=@id AND u.IsActive=1 AND r.Role=@role
              AND ((r.TenantId=@tenant AND t.IsActive=1 AND b.IsActive=1) OR (r.TenantId IS NULL AND r.Role='SuperAdmin'));
            """;
        command.Parameters.AddWithValue("@id", _actorId); command.Parameters.AddWithValue("@tenant", TenantId);
        command.Parameters.AddWithValue("@branch", BranchId);
        command.Parameters.AddWithValue("@role", _actorRole.ToString());
        var features = command.ExecuteScalar();
        if (features is null)
            throw new UnauthorizedAccessException("Your account or organization is no longer active. Sign in again.");
        _subscriptionFeatures = (SubscriptionFeatures)Convert.ToInt32(features);
    }

    public string NextId(string prefix, int start, int count)
    {
        _ = count; // Kept for source compatibility with existing callers.
        string[] tables = prefix switch
        {
            "CL" => ["Clients"],
            "AP" => ["Appointments"],
            "INQ" => ["Inquiries"],
            "REM" => ["Reminders"],
            "PRO" => ["Promotions"],
            "FB" => ["Complaints", "Feedback"],
            "CON" => ["Consultations"],
            "INV" => ["Inventory"],
            _ => []
        };
        var existing = prefix switch
        {
            "CL" => Data.Clients.Select(x => x.Id),
            "AP" => Data.Appointments.Select(x => x.Id),
            "INQ" => Data.Inquiries.Select(x => x.Id),
            "REM" => Data.Reminders.Select(x => x.Id),
            "PRO" => Data.Promotions.Select(x => x.Id),
            "FB" => Data.Feedback.Select(x => x.Id),
            "CON" => Data.Consultations.Select(x => x.Id),
            "INV" => Data.Inventory.Select(x => x.Id),
            _ => []
        };
        var ids = existing.ToList();
        if (tables.Length != 0)
        {
            using var connection = Open();
            InitializeSchema(connection);
            foreach (var table in tables)
            {
                using var command = connection.CreateCommand();
                command.CommandText = $"SELECT Id FROM dbo.{table};";
                using var reader = command.ExecuteReader();
                while (reader.Read()) ids.Add(reader.GetString(0));
            }
        }
        var max = ids.Distinct(StringComparer.Ordinal)
            .Select(x => int.TryParse(x.Split('-').LastOrDefault(), out var value) ? value : start - 1)
            .DefaultIfEmpty(start - 1).Max();
        return $"{prefix}-{Math.Max(start, max + 1)}";
    }

    public IReadOnlyList<AppointmentRecord> GetArchivedAppointments()
    {
        using var connection = Open();
        InitializeSchema(connection);
        return LoadCollection<AppointmentRecord>(connection, "Appointments", isDeleted: true).ToArray();
    }

    public void AddActivity(string icon, string title, string detail)
    {
        Data.Activities.Insert(0, new ActivityRecord { Id = Guid.NewGuid().ToString("N"), Icon = icon, Title = title, Detail = detail, Time = "Just now" });
        while (Data.Activities.Count > 12)
            Data.Activities.RemoveAt(Data.Activities.Count - 1);
    }

    public int PendingSyncEvents()
    {
        using var connection = Open();
        InitializeSchema(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM OutboxEvents WHERE SyncState='Pending';";
        return Convert.ToInt32(command.ExecuteScalar());
    }

    public IReadOnlyList<AuditLogRecord> GetAuditLogs(int limit = 250)
    {
        using var connection = Open();
        InitializeSchema(connection);
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT TOP (@limit) Id,OccurredUtc,ActorName,Action,EntityType,EntityId,Details FROM dbo.TenantAuditLog ORDER BY Id DESC;";
        command.Parameters.AddWithValue("@limit", limit);
        using var reader = command.ExecuteReader();
        var records = new List<AuditLogRecord>();
        while (reader.Read())
        {
            records.Add(new AuditLogRecord
            {
                Id = reader.GetInt64(0), OccurredUtc = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc), ActorUsername = reader.GetString(2),
                TenantId = TenantId, Action = reader.GetString(3), EntityType = reader.GetString(4), EntityId = reader.GetString(5), Details = reader.GetString(6)
            });
        }
        return records;
    }

    private string CanonicalPayload<T>(string payload)
    {
        var value = JsonSerializer.Deserialize<T>(payload, _jsonOptions);
        return value is null ? payload : JsonSerializer.Serialize(value, _jsonOptions);
    }

    private bool SaveCollection<T>(SqlConnection connection, SqlTransaction transaction, string table, IEnumerable<T> records, Func<T, string> idSelector)
    {
        var existing = ReadStoredEntities(connection, transaction, table);
        var activeIds = new HashSet<string>(StringComparer.Ordinal);
        var changed = false;
        foreach (var record in records)
        {
            var id = idSelector(record);
            if (string.IsNullOrWhiteSpace(id)) continue;
            activeIds.Add(id);
            var payload = JsonSerializer.Serialize(record, _jsonOptions);
            if (existing.TryGetValue(id, out var stored) && !stored.IsDeleted &&
                CanonicalPayload<T>(stored.Payload) == CanonicalPayload<T>(payload))
                continue;
            AuthorizeChange(table, payload, stored?.Payload);
            var version = existing.TryGetValue(id, out stored) ? stored.Version + 1 : 1;
            UpsertEntity(connection, transaction, table, id, payload, version, false);
            AddOutboxAndAudit(connection, transaction, table, id, stored?.IsDeleted == true ? "Restore" : "Upsert", version, payload);
            if (table == "Promotions" && stored?.Payload is not null)
            {
                var before = JsonSerializer.Deserialize<PromotionRecord>(stored.Payload, _jsonOptions);
                var after = JsonSerializer.Deserialize<PromotionRecord>(payload, _jsonOptions);
                if (before?.Approval != after?.Approval && after is not null)
                    AddBusinessAudit(connection, transaction, after.Approval switch
                    {
                        "Approved" => "Approve promotion",
                        "Rejected" => "Reject promotion",
                        "Needs review" => "Submit promotion",
                        _ => "Revise promotion"
                    }, id,
                        $"{after.Name} · {after.Approval} · {after.RejectionReason}");
            }
            changed = true;
        }

        foreach (var pair in existing.Where(x => !x.Value.IsDeleted && !activeIds.Contains(x.Key)))
        {
            AuthorizeChange(table, null, pair.Value.Payload);
            var version = pair.Value.Version + 1;
            UpsertEntity(connection, transaction, table, pair.Key, pair.Value.Payload, version, true);
            AddOutboxAndAudit(connection, transaction, table, pair.Key, "Delete", version, "{}");
            changed = true;
        }
        return changed;
    }

    private void AuthorizeChange(string table, string? payload, string? previous)
    {
        if (_actorRole is null || table == "Activities") return; // Bootstrap and smoke-test stores have no signed-in actor.
        var feature = table switch
        {
            "Clients" => SubscriptionFeatures.Clients,
            "Appointments" => SubscriptionFeatures.Appointments,
            "Inquiries" or "Complaints" or "Feedback" => SubscriptionFeatures.Collection,
            "Reminders" => SubscriptionFeatures.Reminders,
            "Promotions" => SubscriptionFeatures.Promotions,
            "Consultations" => SubscriptionFeatures.Clinical,
            "Inventory" => SubscriptionFeatures.Inventory,
            _ => SubscriptionFeatures.None
        };
        if (feature != SubscriptionFeatures.None && !_subscriptionFeatures.HasFlag(feature))
            throw new UnauthorizedAccessException("This module is not included in the company's subscription.");
        var permitted = table switch
        {
            "Clients" => _actorRole is UserRole.Receptionist or UserRole.Admin,
            "Appointments" => _actorRole is UserRole.Receptionist or UserRole.Veterinarian or UserRole.Admin,
            "Inquiries" => _actorRole is UserRole.Receptionist or UserRole.Manager,
            "Reminders" => _actorRole == UserRole.Receptionist,
            "Complaints" or "Feedback" => _actorRole is UserRole.Receptionist or UserRole.Manager or UserRole.Admin,
            "Promotions" => _actorRole is UserRole.Receptionist or UserRole.Admin,
            "Consultations" => _actorRole == UserRole.Veterinarian,
            "Inventory" => _actorRole is UserRole.Veterinarian or UserRole.Admin,
            _ => false
        };
        if (!permitted) throw new UnauthorizedAccessException($"Your role cannot change {table.ToLowerInvariant()}.");
        if (table != "Promotions" || payload is null) return;
        var current = JsonSerializer.Deserialize<PromotionRecord>(payload, _jsonOptions)!;
        var old = previous is null ? null : JsonSerializer.Deserialize<PromotionRecord>(previous, _jsonOptions);
        var materialChanged = old is not null &&
            (current.Name != old.Name || current.Code != old.Code || current.OfferValue != old.OfferValue || current.Goal != old.Goal ||
             current.Audience != old.Audience || current.Channel != old.Channel || current.StartDate != old.StartDate || current.EndDate != old.EndDate);
        if (materialChanged && old?.Approval != "Draft" && current.Approval == old?.Approval)
            throw new InvalidOperationException("Revising campaign terms must return the campaign to draft for a new review.");
        var resetForRevision = old is not null && materialChanged && current.Approval == "Draft" && current.Status == "Draft" &&
            current.ApprovedBy == "" && current.ApprovedUtc is null && current.RejectionReason == "";
        var submitted = old?.Approval is "Draft" or "Rejected" && current.Approval == "Needs review" && current.Status == "Draft" &&
            current.SubmittedBy == _actorName && current.SubmittedUtc is not null && current.ApprovedBy == "" && current.ApprovedUtc is null && current.RejectionReason == "";
        var newDraft = old is null && current.Approval is "Draft" or "Needs review" && current.Status == "Draft" &&
            current.ApprovedBy == "" && current.ApprovedUtc is null && current.RejectionReason == "";
        var reviewStateChanged = current.Approval != (old?.Approval ?? current.Approval) || current.ApprovedBy != (old?.ApprovedBy ?? "") ||
            current.ApprovedUtc != old?.ApprovedUtc || current.RejectionReason != (old?.RejectionReason ?? "");
        if (_actorRole != UserRole.Admin && reviewStateChanged && !resetForRevision && !submitted && !newDraft)
            throw new UnauthorizedAccessException("Only a branch administrator can approve or reject an offer.");
        var validationError = PromotionWorkflowService.ValidateForPersistence(current);
        if (validationError is not null) throw new InvalidOperationException(validationError);
    }

    private void ValidatePromotionCodes()
    {
        var duplicate = Data.Promotions.Where(x => !string.IsNullOrWhiteSpace(x.Code))
            .GroupBy(x => x.Code.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(x => x.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Offer code {duplicate.Key} is used by more than one campaign. Choose a unique code.");
    }

    private Dictionary<string, StoredEntity> ReadStoredEntities(SqlConnection connection, SqlTransaction transaction, string table)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT Id,Payload,Version,IsDeleted FROM dbo.{table};";
        using var reader = command.ExecuteReader();
        var records = new Dictionary<string, StoredEntity>(StringComparer.Ordinal);
        while (reader.Read()) records[reader.GetString(0)] = new StoredEntity(reader.GetString(1), reader.GetInt64(2), reader.GetBoolean(3));
        return records;
    }

    private void UpsertEntity(SqlConnection connection, SqlTransaction transaction, string table, string id, string payload, long version, bool deleted)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"""
            UPDATE dbo.{table} WITH (UPDLOCK)
            SET Payload=@payload,Version=@version,UpdatedUtc=@updated,DeviceId=@device,IsDeleted=@deleted
            WHERE Id=@id;
            IF @@ROWCOUNT=0
                INSERT INTO dbo.{table}(Id,Payload,Version,UpdatedUtc,DeviceId,IsDeleted)
                VALUES(@id,@payload,@version,@updated,@device,@deleted);
            """;
        command.Parameters.AddWithValue("@id", id); command.Parameters.AddWithValue("@payload", payload); command.Parameters.AddWithValue("@version", version);
        command.Parameters.AddWithValue("@updated", DateTime.UtcNow); command.Parameters.AddWithValue("@device", DeviceId);
        command.Parameters.AddWithValue("@deleted", deleted); command.ExecuteNonQuery();
    }

    private void AddOutboxAndAudit(SqlConnection connection, SqlTransaction transaction, string entityType, string entityId, string operation, long version, string payload)
    {
        using var outbox = connection.CreateCommand();
        outbox.Transaction = transaction;
        outbox.CommandText = """
            INSERT INTO dbo.OutboxEvents(EventId,IdempotencyKey,EntityType,EntityId,Operation,Version,Payload,OccurredUtc,SyncState)
            VALUES(@event,@key,@type,@entity,@operation,@version,@payload,@time,'Pending');
            """;
        outbox.Parameters.AddWithValue("@event", Guid.NewGuid().ToString("N")); outbox.Parameters.AddWithValue("@key", $"{TenantId}:{BranchId}:{entityType}:{entityId}:{version}");
        outbox.Parameters.AddWithValue("@type", entityType); outbox.Parameters.AddWithValue("@entity", entityId); outbox.Parameters.AddWithValue("@operation", operation);
        outbox.Parameters.AddWithValue("@version", version); outbox.Parameters.AddWithValue("@payload", payload); outbox.Parameters.AddWithValue("@time", DateTime.UtcNow);
        outbox.ExecuteNonQuery();

        using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO dbo.TenantAuditLog(OccurredUtc,ActorId,ActorName,Action,EntityType,EntityId,Details) VALUES(@time,@actor,@name,@action,@type,@entity,@details);";
        audit.Parameters.AddWithValue("@time", DateTime.UtcNow); audit.Parameters.AddWithValue("@actor", _actorId);
        audit.Parameters.AddWithValue("@name", _actorName); audit.Parameters.AddWithValue("@action", operation); audit.Parameters.AddWithValue("@type", entityType);
        audit.Parameters.AddWithValue("@entity", entityId); audit.Parameters.AddWithValue("@details", $"Local {operation.ToLowerInvariant()} queued for incremental sync.");
        audit.ExecuteNonQuery();
    }

    private void AddBusinessAudit(SqlConnection connection, SqlTransaction transaction, string action, string entityId, string details)
    {
        using var audit = connection.CreateCommand();
        audit.Transaction = transaction;
        audit.CommandText = "INSERT INTO dbo.TenantAuditLog(OccurredUtc,ActorId,ActorName,Action,EntityType,EntityId,Details) VALUES(@time,@actor,@name,@action,'Promotions',@entity,@details);";
        audit.Parameters.AddWithValue("@time", DateTime.UtcNow); audit.Parameters.AddWithValue("@actor", _actorId);
        audit.Parameters.AddWithValue("@name", _actorName); audit.Parameters.AddWithValue("@action", action);
        audit.Parameters.AddWithValue("@entity", entityId); audit.Parameters.AddWithValue("@details", details);
        audit.ExecuteNonQuery();
    }

    private ObservableCollection<T> LoadCollection<T>(SqlConnection connection, string table, bool isDeleted = false)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT Payload FROM dbo.{table} WHERE IsDeleted=@deleted ORDER BY UpdatedUtc DESC;";
        command.Parameters.AddWithValue("@deleted", isDeleted);
        using var reader = command.ExecuteReader();
        var records = new ObservableCollection<T>();
        while (reader.Read())
        {
            var item = JsonSerializer.Deserialize<T>(reader.GetString(0), _jsonOptions);
            if (item is not null) records.Add(item);
        }
        return records;
    }

    private static void InitializeSchema(SqlConnection connection)
    {
        using var command = connection.CreateCommand();
        var entitySql = string.Join(Environment.NewLine, EntityTables.Select(table => $"""
            IF OBJECT_ID(N'dbo.{table}', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.{table} (
                    Id nvarchar(100) NOT NULL CONSTRAINT PK_{table} PRIMARY KEY,
                    Payload nvarchar(max) NOT NULL,
                    Version bigint NOT NULL,
                    UpdatedUtc datetime2(7) NOT NULL,
                    DeviceId nvarchar(200) NOT NULL,
                    IsDeleted bit NOT NULL CONSTRAINT DF_{table}_IsDeleted DEFAULT 0
                );
            END;
            """));
        command.CommandText = entitySql + """
            IF OBJECT_ID(N'dbo.TenantMetadata', N'U') IS NULL
                CREATE TABLE dbo.TenantMetadata([Key] nvarchar(100) NOT NULL CONSTRAINT PK_TenantMetadata PRIMARY KEY, Value nvarchar(max) NOT NULL);
            IF OBJECT_ID(N'dbo.OutboxEvents', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.OutboxEvents(
                    Sequence bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_OutboxEvents PRIMARY KEY,
                    EventId nvarchar(64) NOT NULL CONSTRAINT UQ_OutboxEvents_EventId UNIQUE,
                    IdempotencyKey nvarchar(300) NOT NULL CONSTRAINT UQ_OutboxEvents_IdempotencyKey UNIQUE,
                    EntityType nvarchar(100) NOT NULL,
                    EntityId nvarchar(100) NOT NULL,
                    Operation nvarchar(30) NOT NULL,
                    Version bigint NOT NULL,
                    Payload nvarchar(max) NOT NULL,
                    OccurredUtc datetime2(7) NOT NULL,
                    SyncState nvarchar(30) NOT NULL CONSTRAINT DF_OutboxEvents_SyncState DEFAULT 'Pending',
                    SyncedUtc datetime2(7) NULL
                );
                CREATE INDEX IX_OutboxEvents_StateSequence ON dbo.OutboxEvents(SyncState, Sequence);
            END;
            IF OBJECT_ID(N'dbo.SyncLease', N'U') IS NULL
                CREATE TABLE dbo.SyncLease(Id int NOT NULL CONSTRAINT PK_SyncLease PRIMARY KEY CONSTRAINT CK_SyncLease_Id CHECK(Id=1), OwnerId nvarchar(200) NOT NULL, ExpiresUtc datetime2(7) NOT NULL);
            IF OBJECT_ID(N'dbo.SyncMetadata', N'U') IS NULL
                CREATE TABLE dbo.SyncMetadata([Key] nvarchar(100) NOT NULL CONSTRAINT PK_SyncMetadata PRIMARY KEY, Value nvarchar(max) NOT NULL);
            IF OBJECT_ID(N'dbo.TenantAuditLog', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.TenantAuditLog(
                    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_TenantAuditLog PRIMARY KEY,
                    OccurredUtc datetime2(7) NOT NULL,
                    ActorId nvarchar(64) NOT NULL,
                    ActorName nvarchar(200) NOT NULL,
                    Action nvarchar(50) NOT NULL,
                    EntityType nvarchar(100) NOT NULL,
                    EntityId nvarchar(100) NOT NULL,
                    Details nvarchar(1000) NOT NULL
                );
                CREATE INDEX IX_TenantAuditLog_Time ON dbo.TenantAuditLog(OccurredUtc DESC);
            END;
            IF NOT EXISTS(SELECT 1 FROM dbo.TenantMetadata WHERE [Key]='DataRevision')
                INSERT INTO dbo.TenantMetadata([Key],Value) VALUES('DataRevision','0');
            """;
        command.ExecuteNonQuery();
    }

    private SqlConnection Open()
    {
        var connection = new SqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static long ReadRevision(SqlConnection connection, SqlTransaction? transaction = null, bool lockForUpdate = false)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT Value FROM dbo.TenantMetadata{(lockForUpdate ? " WITH (UPDLOCK,HOLDLOCK)" : string.Empty)} WHERE [Key]='DataRevision';";
        return long.TryParse(Convert.ToString(command.ExecuteScalar()), out var revision) ? revision : 0;
    }

    private static void WriteMetadata(SqlConnection connection, SqlTransaction transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "UPDATE dbo.TenantMetadata WITH (UPDLOCK) SET Value=@value WHERE [Key]=@key; IF @@ROWCOUNT=0 INSERT INTO dbo.TenantMetadata([Key],Value) VALUES(@key,@value);";
        command.Parameters.AddWithValue("@key", key); command.Parameters.AddWithValue("@value", value); command.ExecuteNonQuery();
    }

    private ClinicData? TryLoadLegacyData()
    {
        if (TenantId != "tenant-a" || BranchId != "tenant-a-main") return null;
        var legacyPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PawCare", "clinic-data.json");
        if (!File.Exists(legacyPath)) return null;
        try { return JsonSerializer.Deserialize<ClinicData>(File.ReadAllText(legacyPath), _jsonOptions); }
        catch { return null; }
    }

    private static ClinicData Seed()
    {
        var today = DateTime.Today;
        return new ClinicData
        {
            Clients =
            [
                new() { Id = "CL-1048", ClientName = "Sofia Reyes", Phone = "0917 554 2890", Email = "sofia.reyes@email.com", Address = "Makati City", MarketingConsent = true, PetName = "Milo", Species = "Dog", Breed = "Shih Tzu", Sex = "Male", Birthday = new DateTime(2022, 4, 18), LastVisit = today.AddDays(-12) },
                new() { Id = "CL-1047", ClientName = "Gabriel Cruz", Phone = "0918 331 9042", Email = "gabriel.c@email.com", Address = "Taguig City", MarketingConsent = true, PetName = "Luna", Species = "Cat", Breed = "Persian", Sex = "Female", Birthday = new DateTime(2021, 9, 2), LastVisit = today.AddDays(-18) },
                new() { Id = "CL-1046", ClientName = "Amelia Santos", Phone = "0920 842 1175", Email = "amelia.s@email.com", Address = "Pasig City", MarketingConsent = false, PetName = "Coco", Species = "Dog", Breed = "Golden Retriever", Sex = "Female", Birthday = new DateTime(2020, 11, 11), LastVisit = today.AddDays(-26) },
                new() { Id = "CL-1045", ClientName = "Daniel Lim", Phone = "0916 279 6681", Email = "daniel.lim@email.com", Address = "Quezon City", MarketingConsent = true, PetName = "Pepper", Species = "Rabbit", Breed = "Mini Rex", Sex = "Male", Birthday = new DateTime(2023, 1, 23), LastVisit = today.AddDays(-34) },
                new() { Id = "CL-1044", ClientName = "Isabella Tan", Phone = "0919 650 4872", Email = "isabella.t@email.com", Address = "Mandaluyong City", MarketingConsent = true, PetName = "Atlas", Species = "Dog", Breed = "Aspin", Sex = "Male", Birthday = new DateTime(2019, 6, 14), LastVisit = today.AddDays(-41) }
            ],
            Appointments =
            [
                new() { Id = "AP-2088", Date = today, Time = "09:00", ClientId = "CL-1048", ClientName = "Sofia Reyes", PetName = "Milo", Service = "Annual wellness exam", Veterinarian = "Dr. Mia Garcia", Status = "Confirmed", Notes = "First visit after grooming allergy." },
                new() { Id = "AP-2089", Date = today, Time = "10:30", ClientId = "CL-1047", ClientName = "Gabriel Cruz", PetName = "Luna", Service = "Core vaccination", Veterinarian = "Dr. Luis Mendoza", Status = "Checked in" },
                new() { Id = "AP-2090", Date = today, Time = "13:00", ClientId = "CL-1046", ClientName = "Amelia Santos", PetName = "Coco", Service = "Dermatology follow-up", Veterinarian = "Dr. Mia Garcia", Status = "Pending", Notes = "Review response to prescribed shampoo." },
                new() { Id = "AP-2091", Date = today, Time = "15:30", ClientId = "CL-1045", ClientName = "Daniel Lim", PetName = "Pepper", Service = "Nail trim & wellness", Veterinarian = "Dr. Ava Villanueva", Status = "Confirmed" },
                new() { Id = "AP-2092", Date = today.AddDays(1), Time = "09:30", ClientId = "CL-1044", ClientName = "Isabella Tan", PetName = "Atlas", Service = "Senior care assessment", Veterinarian = "Dr. Luis Mendoza", Status = "Scheduled" }
            ],
            Inquiries =
            [
                new() { Id = "INQ-3021", Created = today.AddDays(-1), ClientName = "Sofia Reyes", Contact = "0917 554 2890", PetName = "Milo", Channel = "Phone", Category = "Post-visit care", Subject = "Mild redness after grooming", Details = "Asked whether mild skin redness needs an earlier follow-up.", Priority = "Medium", Status = "Follow-up", PreferredChannel = "SMS", CommunicationConsent = true, Owner = "Alex Morgan", FollowUpDate = today },
                new() { Id = "INQ-3020", Created = today.AddDays(-1), ClientName = "New lead — Rafael Ong", Contact = "rafael.ong@email.com", PetName = "Unnamed puppy", Channel = "Website", Category = "New client", Subject = "First puppy wellness package", Details = "Comparing vaccination and wellness package inclusions.", Priority = "Low", Status = "New", PreferredChannel = "Email", CommunicationConsent = true, Owner = "Alex Morgan", FollowUpDate = today.AddDays(1) },
                new() { Id = "INQ-3019", Created = today.AddDays(-2), ClientName = "Gabriel Cruz", Contact = "0918 331 9042", PetName = "Luna", Channel = "Messenger", Category = "Appointment", Subject = "Request to move vaccination schedule", Details = "Client needs an afternoon slot this week.", Priority = "Medium", Status = "Resolved", PreferredChannel = "Messenger", CommunicationConsent = true, Owner = "Alex Morgan" },
                new() { Id = "INQ-3018", Created = today.AddDays(-3), ClientName = "Amelia Santos", Contact = "0920 842 1175", PetName = "Coco", Channel = "Walk-in", Category = "Billing", Subject = "Clarify dermatology package price", Details = "Requested detailed cost breakdown before next visit.", Priority = "High", Status = "Open", PreferredChannel = "Phone", CommunicationConsent = false, Owner = "Alex Morgan", FollowUpDate = today }
            ],
            Reminders =
            [
                new() { Id = "REM-810", ClientName = "Sofia Reyes", PetName = "Milo", Type = "Post-visit check-in", Channel = "SMS", DueDate = today, Status = "Pending", Message = "Hi Sofia, how is Milo feeling after the visit?" },
                new() { Id = "REM-811", ClientName = "Gabriel Cruz", PetName = "Luna", Type = "Vaccination reminder", Channel = "Email", DueDate = today.AddDays(2), Status = "Scheduled", Message = "Luna’s vaccination appointment is coming up." },
                new() { Id = "REM-812", ClientName = "Isabella Tan", PetName = "Atlas", Type = "Appointment reminder", Channel = "SMS", DueDate = today.AddDays(1), Status = "Scheduled", Message = "A friendly reminder for Atlas’s senior care assessment." }
            ],
            Promotions =
            [
                new() { Id = "PRO-110", Name = "Golden Years Wellness", Code = "SENIOR20", OfferValue = "20% off", Audience = "Senior pets (7+ years)", Channel = "SMS + Email", StartDate = today.AddDays(-5), EndDate = today.AddDays(25), Status = "Active", Approval = "Approved", Goal = "Increase senior wellness assessments and early screening uptake.", Reach = 184, Claims = 27, Conversion = 15, CreatedBy = "Alex Morgan" },
                new() { Id = "PRO-109", Name = "New Puppy Welcome", Code = "HELLOPUP", OfferValue = "₱500 off", Audience = "New puppy leads", Channel = "Email", StartDate = today.AddDays(3), EndDate = today.AddDays(35), Status = "Scheduled", Approval = "Approved", Goal = "Convert first-time puppy inquiries into wellness appointments.", Reach = 76, Claims = 12, Conversion = 16, CreatedBy = "Alex Morgan" },
                new() { Id = "PRO-108", Name = "Dental Care Month", Code = "SMILE15", OfferValue = "15% off", Audience = "Pets due for dental care", Channel = "SMS", StartDate = today.AddDays(10), EndDate = today.AddDays(40), Status = "Draft", Approval = "Needs review", Goal = "Re-engage clients with overdue dental assessments.", CreatedBy = "Alex Morgan" }
            ],
            Feedback =
            [
                new() { Id = "FB-512", Date = today.AddDays(-1), ClientName = "Sofia Reyes", Contact = "0917 554 2890", PetName = "Milo", CaseType = "Compliment", Channel = "SMS", Rating = 5, Category = "Service", Priority = "Low", Comment = "The receptionist explained every next step clearly.", Status = "Reviewed", Escalation = "None", ConsentToFollowUp = true, Owner = "Alex Morgan" },
                new() { Id = "FB-511", Date = today.AddDays(-2), ClientName = "Amelia Santos", Contact = "0920 842 1175", PetName = "Coco", CaseType = "Complaint", Channel = "Phone", Rating = 3, Category = "Wait time", Priority = "Medium", Comment = "Coco was seen later than our appointment time.", Status = "Open", Escalation = "Manager review", ConsentToFollowUp = true, Owner = "Alex Morgan", FollowUpDate = today },
                new() { Id = "FB-510", Date = today.AddDays(-4), ClientName = "Daniel Lim", Contact = "0916 279 6681", PetName = "Pepper", CaseType = "Feedback", Channel = "Email", Rating = 5, Category = "Veterinarian", Priority = "Low", Comment = "Very gentle handling and helpful care advice.", Status = "Reviewed", Escalation = "None", ConsentToFollowUp = true, Owner = "Alex Morgan" },
                new() { Id = "FB-509", Date = today.AddDays(-6), ClientName = "Isabella Tan", Contact = "0919 650 4872", PetName = "Atlas", CaseType = "Complaint", Channel = "Walk-in", Rating = 2, Category = "Billing", Priority = "Urgent", Comment = "The final invoice was not the amount I expected.", Status = "Escalated", Escalation = "Urgent", ConsentToFollowUp = true, Owner = "Jordan Lee", FollowUpDate = today, ResolutionNotes = "Manager to review invoice breakdown with the client." }
            ],
            Consultations =
            [
                new() { Id = "CON-100", AppointmentId = "AP-2088", Date = today, ClientName = "Sofia Reyes", PetName = "Milo", Veterinarian = "Dr. Mia Garcia", Assessment = "Mild contact dermatitis", TreatmentPlan = "Continue hypoallergenic shampoo and monitor for seven days.", MedicalNotes = "Skin redness reduced since last contact.", Status = "In progress" },
                new() { Id = "CON-101", AppointmentId = "AP-2089", Date = today, ClientName = "Gabriel Cruz", PetName = "Luna", Veterinarian = "Dr. Luis Mendoza", Assessment = "Fit for vaccination", TreatmentPlan = "Routine observation after vaccination.", Vaccinations = "FVRCP booster", MedicalNotes = "No adverse reaction observed.", Status = "Completed" }
            ],
            Inventory =
            [
                new() { Id = "INV-100", ItemName = "Rabies vaccine", Category = "Vaccines", Quantity = 24, ReorderLevel = 10, Unit = "doses", UpdatedUtc = DateTime.UtcNow },
                new() { Id = "INV-101", ItemName = "FVRCP vaccine", Category = "Vaccines", Quantity = 8, ReorderLevel = 10, Unit = "doses", UpdatedUtc = DateTime.UtcNow },
                new() { Id = "INV-102", ItemName = "Hypoallergenic shampoo", Category = "Treatment", Quantity = 16, ReorderLevel = 6, Unit = "bottles", UpdatedUtc = DateTime.UtcNow }
            ],
            Activities =
            [
                new() { Id = "ACT-100", Icon = "✉", Title = "Inquiry logged for Milo", Detail = "Post-visit care · Phone", Time = "18 min ago" },
                new() { Id = "ACT-101", Icon = "▣", Title = "Luna checked in", Detail = "Core vaccination · Dr. Mendoza", Time = "34 min ago" },
                new() { Id = "ACT-102", Icon = "◇", Title = "Promotion claim recorded", Detail = "Golden Years Wellness", Time = "1 hr ago" },
                new() { Id = "ACT-103", Icon = "●", Title = "Client profile updated", Detail = "Sofia Reyes · Contact consent", Time = "2 hrs ago" }
            ]
        };
    }
}
