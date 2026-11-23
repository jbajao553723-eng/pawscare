using System.Data;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Data.SqlClient;
using PawCare.Desktop.Models;

namespace PawCare.Desktop.Services;

public sealed class MasterDatabase
{
    private const int PasswordIterations = 210_000;
    private readonly DatabasePaths _paths;
    private readonly string _connectionString;

    public string DatabasePath => $@"{_paths.ServerName}\{_paths.MasterDatabaseName}";

    public MasterDatabase(DatabasePaths paths)
    {
        _paths = paths;
        _connectionString = paths.MasterConnectionString;
    }

    public void Initialize()
    {
        EnsureDatabase(_paths.MasterDatabaseName);
        foreach (var tenantId in new[] { "tenant-a", "tenant-b", "tenant-c" }.Where(_paths.ManagesTenant))
            EnsureDatabase(_paths.TenantDatabaseName(tenantId));

        using var connection = Open();
        Execute(connection, """
            IF OBJECT_ID(N'dbo.Tenants', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Tenants (
                    Id nvarchar(50) NOT NULL CONSTRAINT PK_Tenants PRIMARY KEY,
                    Code nvarchar(20) NOT NULL CONSTRAINT UQ_Tenants_Code UNIQUE,
                    Name nvarchar(150) NOT NULL,
                    DatabasePath nvarchar(128) NOT NULL CONSTRAINT UQ_Tenants_Database UNIQUE,
                    SyncEndpoint nvarchar(500) NOT NULL,
                    IsActive bit NOT NULL CONSTRAINT DF_Tenants_IsActive DEFAULT 1,
                    CreatedUtc datetime2(7) NOT NULL
                );
            END;

            IF OBJECT_ID(N'dbo.Users', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.Users (
                    Id nvarchar(64) NOT NULL CONSTRAINT PK_Users PRIMARY KEY,
                    Username nvarchar(100) NOT NULL,
                    NormalizedUsername nvarchar(100) NOT NULL CONSTRAINT UQ_Users_NormalizedUsername UNIQUE,
                    PasswordHash varbinary(64) NOT NULL,
                    PasswordSalt varbinary(32) NOT NULL,
                    FullName nvarchar(200) NOT NULL,
                    Initials nvarchar(10) NOT NULL,
                    IsActive bit NOT NULL CONSTRAINT DF_Users_IsActive DEFAULT 1,
                    Version bigint NOT NULL CONSTRAINT DF_Users_Version DEFAULT 1,
                    CreatedUtc datetime2(7) NOT NULL,
                    UpdatedUtc datetime2(7) NOT NULL
                );
            END;

            IF OBJECT_ID(N'dbo.UserTenantRoles', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.UserTenantRoles (
                    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_UserTenantRoles PRIMARY KEY,
                    UserId nvarchar(64) NOT NULL,
                    TenantId nvarchar(50) NULL,
                    Role nvarchar(40) NOT NULL,
                    IsPrimary bit NOT NULL CONSTRAINT DF_UserTenantRoles_IsPrimary DEFAULT 1,
                    Version bigint NOT NULL CONSTRAINT DF_UserTenantRoles_Version DEFAULT 1,
                    CONSTRAINT FK_UserTenantRoles_Users FOREIGN KEY(UserId) REFERENCES dbo.Users(Id),
                    CONSTRAINT FK_UserTenantRoles_Tenants FOREIGN KEY(TenantId) REFERENCES dbo.Tenants(Id),
                    CONSTRAINT UQ_UserTenantRoles UNIQUE(UserId, TenantId, Role)
                );
                CREATE INDEX IX_UserTenantRoles_TenantRole ON dbo.UserTenantRoles(TenantId, Role);
            END;

            IF OBJECT_ID(N'dbo.MasterAuditLog', N'U') IS NULL
            BEGIN
                CREATE TABLE dbo.MasterAuditLog (
                    Id bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_MasterAuditLog PRIMARY KEY,
                    OccurredUtc datetime2(7) NOT NULL,
                    ActorUserId nvarchar(64) NOT NULL,
                    ActorUsername nvarchar(200) NOT NULL,
                    TenantId nvarchar(50) NOT NULL,
                    Action nvarchar(100) NOT NULL,
                    EntityType nvarchar(100) NOT NULL,
                    EntityId nvarchar(100) NOT NULL,
                    Details nvarchar(1000) NOT NULL
                );
                CREATE INDEX IX_MasterAuditLog_TenantTime ON dbo.MasterAuditLog(TenantId, OccurredUtc DESC);
            END;
            """);

        Execute(connection, """
            IF OBJECT_ID(N'dbo.SubscriptionPlans', N'U') IS NULL
                CREATE TABLE dbo.SubscriptionPlans (
                    Code nvarchar(30) NOT NULL CONSTRAINT PK_SubscriptionPlans PRIMARY KEY,
                    Name nvarchar(100) NOT NULL,
                    Features int NOT NULL,
                    MaxBranches int NOT NULL,
                    IsActive bit NOT NULL,
                    UpdatedUtc datetime2(7) NOT NULL
                );
            IF NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE Code='MAIN')
                INSERT INTO dbo.SubscriptionPlans VALUES('MAIN','Main Transaction',679,1,1,SYSUTCDATETIME());
            IF NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE Code='PROMO')
                INSERT INTO dbo.SubscriptionPlans VALUES('PROMO','Promotional',1727,1,1,SYSUTCDATETIME());
            IF NOT EXISTS (SELECT 1 FROM dbo.SubscriptionPlans WHERE Code='FULL')
                INSERT INTO dbo.SubscriptionPlans VALUES('FULL','Full Clinic',4095,20,1,SYSUTCDATETIME());
            UPDATE dbo.SubscriptionPlans SET Features=551,UpdatedUtc=SYSUTCDATETIME() WHERE Code='MAIN' AND Features=39;
            UPDATE dbo.SubscriptionPlans SET Features=1599,UpdatedUtc=SYSUTCDATETIME() WHERE Code='PROMO' AND Features=63;
            UPDATE dbo.SubscriptionPlans SET Features=4095,UpdatedUtc=SYSUTCDATETIME() WHERE Code='FULL' AND Features=511;
            UPDATE dbo.SubscriptionPlans SET Features=Features | 128,UpdatedUtc=SYSUTCDATETIME()
            WHERE Code IN ('MAIN','PROMO') AND Features & 128 = 0;
            IF COL_LENGTH('dbo.Tenants','SubscriptionPlanCode') IS NULL
            BEGIN
                ALTER TABLE dbo.Tenants ADD SubscriptionPlanCode nvarchar(30) NOT NULL
                    CONSTRAINT DF_Tenants_SubscriptionPlanCode DEFAULT 'FULL';
                EXEC(N'UPDATE dbo.Tenants SET SubscriptionPlanCode=CASE Id
                    WHEN ''tenant-a'' THEN ''MAIN'' WHEN ''tenant-b'' THEN ''PROMO'' ELSE ''FULL'' END');
            END;
            IF OBJECT_ID(N'dbo.Branches', N'U') IS NULL
                CREATE TABLE dbo.Branches (
                    Id nvarchar(50) NOT NULL CONSTRAINT PK_Branches PRIMARY KEY,
                    TenantId nvarchar(50) NOT NULL CONSTRAINT FK_Branches_Tenants REFERENCES dbo.Tenants(Id),
                    Code nvarchar(20) NOT NULL,
                    Name nvarchar(150) NOT NULL,
                    DatabasePath nvarchar(128) NOT NULL CONSTRAINT UQ_Branches_Database UNIQUE,
                    IsPrimary bit NOT NULL,
                    IsActive bit NOT NULL,
                    CONSTRAINT UQ_Branches_TenantCode UNIQUE(TenantId,Code)
                );
            INSERT INTO dbo.Branches(Id,TenantId,Code,Name,DatabasePath,IsPrimary,IsActive)
            SELECT CONCAT(t.Id,'-main'),t.Id,'MAIN',t.Name,t.DatabasePath,1,1
            FROM dbo.Tenants t WHERE NOT EXISTS(SELECT 1 FROM dbo.Branches b WHERE b.TenantId=t.Id AND b.IsPrimary=1);
            """);

        using (var tenantTransaction = connection.BeginTransaction(IsolationLevel.Serializable))
        {
            if (_paths.ManagesTenant("tenant-a") && !TenantExists(connection, tenantTransaction, "tenant-a"))
                SeedTenant(connection, tenantTransaction, "tenant-a", "PAW-A", "PawCare Makati", _paths.TenantDatabaseName("tenant-a"));
            if (_paths.ManagesTenant("tenant-b") && !TenantExists(connection, tenantTransaction, "tenant-b"))
                SeedTenant(connection, tenantTransaction, "tenant-b", "PAW-B", "PawCare Taguig", _paths.TenantDatabaseName("tenant-b"));
            if (_paths.ManagesTenant("tenant-c") && !TenantExists(connection, tenantTransaction, "tenant-c"))
                SeedTenant(connection, tenantTransaction, "tenant-c", "PAW-C", "PawCare Quezon City", _paths.TenantDatabaseName("tenant-c"));
            tenantTransaction.Commit();
        }

        using var userTransaction = connection.BeginTransaction(IsolationLevel.Serializable);
        if (_paths.ManagesTenant("tenant-a"))
        {
            SeedUserIfMissing(connection, userTransaction, "receptionista", "Alex Morgan", "AM", UserRole.Receptionist, "tenant-a");
            SeedUserIfMissing(connection, userTransaction, "managera", "Jordan Lee", "JL", UserRole.Manager, "tenant-a");
            SeedUserIfMissing(connection, userTransaction, "veterinariana", "Dr. Mia Garcia", "MG", UserRole.Veterinarian, "tenant-a");
            SeedUserIfMissing(connection, userTransaction, "admina", "Casey Rivera", "CR", UserRole.Admin, "tenant-a");
        }
        if (_paths.ManagesTenant("tenant-b"))
        {
            SeedUserIfMissing(connection, userTransaction, "receptionistb", "Sam Reyes", "SR", UserRole.Receptionist, "tenant-b");
            SeedUserIfMissing(connection, userTransaction, "managerb", "Jamie Tan", "JT", UserRole.Manager, "tenant-b");
            SeedUserIfMissing(connection, userTransaction, "veterinarianb", "Dr. Noah Lim", "NL", UserRole.Veterinarian, "tenant-b");
            SeedUserIfMissing(connection, userTransaction, "adminb", "Morgan Yu", "MY", UserRole.Admin, "tenant-b");
        }
        if (_paths.ManagesTenant("tenant-c"))
        {
            SeedUserIfMissing(connection, userTransaction, "receptionistc", "Riley Cruz", "RC", UserRole.Receptionist, "tenant-c");
            SeedUserIfMissing(connection, userTransaction, "managerc", "Drew Santos", "DS", UserRole.Manager, "tenant-c");
            SeedUserIfMissing(connection, userTransaction, "veterinarianc", "Dr. Zoe Ramos", "ZR", UserRole.Veterinarian, "tenant-c");
            SeedUserIfMissing(connection, userTransaction, "adminc", "Taylor Cruz", "TC", UserRole.Admin, "tenant-c");
        }
        SeedUserIfMissing(connection, userTransaction, "superadmin", "Avery Santos", "AS", UserRole.SuperAdmin, null);
        userTransaction.Commit();
    }

    public UserSession? Authenticate(string username, string password)
    {
        var normalized = NormalizeUsername(username);
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (1) u.Id, u.Username, u.PasswordHash, u.PasswordSalt, u.FullName, u.Initials,
                   r.Role, COALESCE(t.Id, ''), COALESCE(t.Name, 'All organizations')
            FROM dbo.Users u
            JOIN dbo.UserTenantRoles r ON r.UserId = u.Id AND r.IsPrimary = 1
            LEFT JOIN dbo.Tenants t ON t.Id = r.TenantId
            WHERE u.NormalizedUsername = @username AND u.IsActive = 1
              AND (t.IsActive = 1 OR r.TenantId IS NULL)
            ORDER BY CASE WHEN r.Role = 'SuperAdmin' THEN 0 ELSE 1 END;
            """;
        command.Parameters.AddWithValue("@username", normalized);
        using var reader = command.ExecuteReader();
        if (!reader.Read()) return null;

        var expectedHash = (byte[])reader[2];
        var salt = (byte[])reader[3];
        var actualHash = HashPassword(password, salt);
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, actualHash)) return null;
        if (!Enum.TryParse<UserRole>(reader.GetString(6), out var role)) return null;

        return new UserSession(role, reader.GetString(4), reader.GetString(5), RoleTitle(role),
            reader.GetString(0), reader.GetString(7), reader.GetString(8));
    }

    public IReadOnlyList<TenantRecord> GetTenants()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = """
            SELECT t.Id,t.Code,t.Name,t.DatabasePath,t.SyncEndpoint,t.IsActive,
                   t.SubscriptionPlanCode,p.Name,p.Features,p.MaxBranches
            FROM dbo.Tenants t JOIN dbo.SubscriptionPlans p ON p.Code=t.SubscriptionPlanCode ORDER BY t.Code;
            """;
        using var reader = command.ExecuteReader();
        var records = new List<TenantRecord>();
        while (reader.Read())
        {
            records.Add(new TenantRecord
            {
                Id = reader.GetString(0), Code = reader.GetString(1), Name = reader.GetString(2),
                DatabasePath = reader.GetString(3), SyncEndpoint = reader.GetString(4), IsActive = reader.GetBoolean(5),
                SubscriptionPlanCode = reader.GetString(6), SubscriptionPlanName = reader.GetString(7),
                SubscriptionFeatures = (SubscriptionFeatures)reader.GetInt32(8), MaxBranches = reader.GetInt32(9)
            });
        }
        return records;
    }

    public TenantRecord GetTenant(string tenantId) =>
        GetTenants().FirstOrDefault(x => x.Id == tenantId)
        ?? throw new InvalidOperationException("The assigned organization is not available.");

    public IReadOnlyList<StaffAccountRecord> GetStaff(string? tenantId = null, UserRole? role = null)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 120;
        command.CommandText = """
            SELECT u.Id,u.Username,u.FullName,u.Initials,r.Role,COALESCE(t.Id,''),
                   COALESCE(t.Name,'All organizations'),u.IsActive,u.Version
            FROM dbo.Users u
            JOIN dbo.UserTenantRoles r ON r.UserId=u.Id
            LEFT JOIN dbo.Tenants t ON t.Id=r.TenantId
            WHERE (@tenant='' OR COALESCE(r.TenantId,'')=@tenant)
              AND (@role='' OR r.Role=@role)
            ORDER BY t.Code,r.Role,u.FullName;
            """;
        command.Parameters.AddWithValue("@tenant", tenantId ?? string.Empty);
        command.Parameters.AddWithValue("@role", role?.ToString() ?? string.Empty);
        using var reader = command.ExecuteReader();
        var records = new List<StaffAccountRecord>();
        while (reader.Read())
        {
            records.Add(new StaffAccountRecord
            {
                UserId = reader.GetString(0), Username = reader.GetString(1), FullName = reader.GetString(2), Initials = reader.GetString(3),
                Role = Enum.Parse<UserRole>(reader.GetString(4)), TenantId = reader.GetString(5), TenantName = reader.GetString(6),
                IsActive = reader.GetBoolean(7), Version = reader.GetInt64(8)
            });
        }
        return records;
    }

    public void SaveBranchStaff(UserSession actor, StaffAccountRecord? existing, string username, string fullName, UserRole role, string? password)
    {
        EnsureActorActive(actor);
        if (actor.Role != UserRole.Admin || string.IsNullOrWhiteSpace(actor.TenantId))
            throw new UnauthorizedAccessException("Only a branch administrator can manage branch staff.");
        if (role is not (UserRole.Receptionist or UserRole.Manager or UserRole.Veterinarian))
            throw new UnauthorizedAccessException("Branch administrators can assign Receptionist, Manager, or Veterinarian roles.");
        if (existing is not null && (existing.TenantId != actor.TenantId || existing.Role is UserRole.Admin or UserRole.SuperAdmin))
            throw new UnauthorizedAccessException("This account is outside your branch staff scope.");
        SaveAccount(actor, existing, actor.TenantId, username, fullName, role, password);
    }

    public void SaveBranchAdmin(UserSession actor, StaffAccountRecord? existing, string tenantId, string username, string fullName, string? password)
    {
        EnsureActorActive(actor);
        if (actor.Role != UserRole.SuperAdmin)
            throw new UnauthorizedAccessException("Only the Super Admin can assign branch administrators.");
        if (existing is not null && existing.Role != UserRole.Admin)
            throw new UnauthorizedAccessException("Only branch administrator accounts can be changed here.");
        if (GetTenants().All(x => x.Id != tenantId || !x.IsActive))
            throw new InvalidOperationException("Select an active organization.");
        SaveAccount(actor, existing, tenantId, username, fullName, UserRole.Admin, password);
    }

    public void SetAccountActive(UserSession actor, StaffAccountRecord account, bool active)
    {
        EnsureActorActive(actor);
        if (actor.Role != UserRole.SuperAdmin && (actor.Role != UserRole.Admin || actor.TenantId != account.TenantId || account.Role is UserRole.Admin or UserRole.SuperAdmin))
            throw new UnauthorizedAccessException("You cannot change this account's access.");
        if (actor.UserId == account.UserId && !active) throw new InvalidOperationException("You cannot deactivate your own account.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        using var update = connection.CreateCommand();
        update.Transaction = transaction;
        update.CommandText = "UPDATE dbo.Users SET IsActive=@active,Version=Version+1,UpdatedUtc=@now WHERE Id=@id AND Version=@version;";
        update.Parameters.AddWithValue("@active", active); update.Parameters.AddWithValue("@now", DateTime.UtcNow);
        update.Parameters.AddWithValue("@id", account.UserId); update.Parameters.AddWithValue("@version", account.Version);
        if (update.ExecuteNonQuery() != 1) throw new DataConcurrencyException("The account changed in another session. Reload and try again.");
        AddAudit(connection, transaction, actor, account.TenantId, active ? "Reactivate account" : "Deactivate account", "User", account.UserId, account.Username);
        transaction.Commit();
    }

    public void SaveTenant(UserSession actor, TenantRecord? existing, string code, string name, bool active, string? subscriptionPlanCode = null)
    {
        EnsureActorActive(actor);
        if (actor.Role != UserRole.SuperAdmin) throw new UnauthorizedAccessException("Only Super Admin can manage organizations.");
        code = code.Trim().ToUpperInvariant(); name = name.Trim();
        if (code.Length < 3 || code.Length > 20 || !code.All(x => char.IsAsciiLetterOrDigit(x) || x == '-'))
            throw new InvalidOperationException("Use a unique organization code of 3–20 letters, numbers, or hyphens.");
        if (name.Length < 3) throw new InvalidOperationException("Enter an organization name.");
        if (GetTenants().Any(x => x.Code == code && x.Id != existing?.Id))
            throw new InvalidOperationException("That organization code is already in use.");
        subscriptionPlanCode ??= existing?.SubscriptionPlanCode ?? "FULL";
        var selectedPlan = GetSubscriptionPlans().FirstOrDefault(x => x.Code == subscriptionPlanCode && x.IsActive);
        if (selectedPlan is null)
            throw new InvalidOperationException("Select an active subscription plan.");
        if (existing is not null && GetBranches(existing.Id).Count(x => x.IsActive) > selectedPlan.MaxBranches)
            throw new InvalidOperationException("Deactivate extra branches before assigning this subscription.");
        var id = existing?.Id ?? $"tenant-{Guid.NewGuid():N}";
        var databaseName = existing?.DatabasePath ?? $"{_paths.DatabasePrefix}Tenant{Guid.NewGuid():N}";
        if (existing is null) EnsureDatabase(databaseName);
        using var connection = Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = existing is null
            ? "INSERT INTO dbo.Tenants(Id,Code,Name,DatabasePath,SyncEndpoint,IsActive,CreatedUtc,SubscriptionPlanCode) VALUES(@id,@code,@name,@database,@endpoint,@active,@now,@plan);"
            : "UPDATE dbo.Tenants SET Code=@code,Name=@name,IsActive=@active,SubscriptionPlanCode=@plan WHERE Id=@id;";
        command.Parameters.AddWithValue("@id", id); command.Parameters.AddWithValue("@code", code); command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@database", databaseName); command.Parameters.AddWithValue("@endpoint", $"cloud-sync://{id}");
        command.Parameters.AddWithValue("@active", active); command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        command.Parameters.AddWithValue("@plan", subscriptionPlanCode);
        command.ExecuteNonQuery();
        if (existing is null)
        {
            using var branch = connection.CreateCommand();
            branch.Transaction = transaction;
            branch.CommandText = "INSERT INTO dbo.Branches(Id,TenantId,Code,Name,DatabasePath,IsPrimary,IsActive) VALUES(@branch,@tenant,'MAIN',@name,@database,1,1);";
            branch.Parameters.AddWithValue("@branch", id + "-main"); branch.Parameters.AddWithValue("@tenant", id);
            branch.Parameters.AddWithValue("@name", name); branch.Parameters.AddWithValue("@database", databaseName);
            branch.ExecuteNonQuery();
        }
        AddAudit(connection, transaction, actor, id, existing is null ? "Create organization" : "Update organization", "Tenant", id, $"{code} · {name} · {(active ? "Active" : "Inactive")}");
        transaction.Commit();
    }

    public IReadOnlyList<BranchRecord> GetBranches(string tenantId)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Id,TenantId,Code,Name,DatabasePath,IsPrimary,IsActive FROM dbo.Branches WHERE TenantId=@tenant ORDER BY IsPrimary DESC,Code;";
        command.Parameters.AddWithValue("@tenant", tenantId);
        using var reader = command.ExecuteReader();
        var result = new List<BranchRecord>();
        while (reader.Read()) result.Add(new BranchRecord
        {
            Id = reader.GetString(0), TenantId = reader.GetString(1), Code = reader.GetString(2), Name = reader.GetString(3),
            DatabasePath = reader.GetString(4), IsPrimary = reader.GetBoolean(5), IsActive = reader.GetBoolean(6)
        });
        return result;
    }

    public void SaveBranch(UserSession actor, BranchRecord? existing, string tenantId, string code, string name, bool active)
    {
        EnsureActorActive(actor);
        if (actor.Role is not (UserRole.Admin or UserRole.SuperAdmin) || (actor.Role == UserRole.Admin && actor.TenantId != tenantId))
            throw new UnauthorizedAccessException("You cannot manage this company's branches.");
        var tenant = GetTenant(tenantId);
        if (!tenant.SubscriptionFeatures.HasFlag(SubscriptionFeatures.Branching))
            throw new UnauthorizedAccessException("Branching is not included in this subscription.");
        if (existing is not null && existing.TenantId != tenantId)
            throw new UnauthorizedAccessException("This branch belongs to another company.");
        if (existing is null && GetBranches(tenantId).Count(x => x.IsActive) >= tenant.MaxBranches)
            throw new InvalidOperationException("This subscription's branch limit has been reached.");
        if (existing is { IsActive: false } && active && GetBranches(tenantId).Count(x => x.IsActive) >= tenant.MaxBranches)
            throw new InvalidOperationException("This subscription's branch limit has been reached.");
        if (existing?.IsPrimary == true && !active)
            throw new InvalidOperationException("The primary branch cannot be deactivated.");
        code = code.Trim().ToUpperInvariant(); name = name.Trim();
        if (code.Length is < 2 or > 20 || !code.All(x => char.IsAsciiLetterOrDigit(x) || x == '-') || name.Length < 3)
            throw new InvalidOperationException("Enter a branch code of 2–20 letters, numbers, or hyphens and a branch name.");
        if (GetBranches(tenantId).Any(x => string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase) && x.Id != existing?.Id))
            throw new InvalidOperationException("That branch code is already in use for this organization.");
        var id = existing?.Id ?? $"branch-{Guid.NewGuid():N}";
        var databaseName = existing?.DatabasePath ?? $"{tenant.DatabasePath}_{code.Replace('-', '_')}";
        if (existing is null) EnsureDatabase(databaseName);
        using var connection = Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = existing is null
            ? "INSERT INTO dbo.Branches(Id,TenantId,Code,Name,DatabasePath,IsPrimary,IsActive) VALUES(@id,@tenant,@code,@name,@database,0,@active);"
            : "UPDATE dbo.Branches SET Code=@code,Name=@name,IsActive=@active WHERE Id=@id AND TenantId=@tenant;";
        command.Parameters.AddWithValue("@id", id); command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@code", code); command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@database", databaseName); command.Parameters.AddWithValue("@active", active);
        command.ExecuteNonQuery();
        AddAudit(connection, transaction, actor, tenantId, existing is null ? "Create branch" : "Update branch", "Branch", id, $"{code} · {name} · {(active ? "Active" : "Inactive")}");
        transaction.Commit();
    }

    public IReadOnlyList<SubscriptionPlanRecord> GetSubscriptionPlans()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT Code,Name,Features,MaxBranches,IsActive FROM dbo.SubscriptionPlans ORDER BY Code;";
        using var reader = command.ExecuteReader();
        var result = new List<SubscriptionPlanRecord>();
        while (reader.Read()) result.Add(new SubscriptionPlanRecord
        {
            Code = reader.GetString(0), Name = reader.GetString(1), Features = (SubscriptionFeatures)reader.GetInt32(2),
            MaxBranches = reader.GetInt32(3), IsActive = reader.GetBoolean(4)
        });
        return result;
    }

    public void SaveSubscriptionPlan(UserSession actor, SubscriptionPlanRecord? existing, string code, string name,
        SubscriptionFeatures features, int maxBranches, bool active)
    {
        EnsureActorActive(actor);
        if (actor.Role != UserRole.SuperAdmin) throw new UnauthorizedAccessException("Only Super Admin can manage subscriptions.");
        code = code.Trim().ToUpperInvariant(); name = name.Trim();
        if (code.Length is < 2 or > 30 || !code.All(x => char.IsAsciiLetterOrDigit(x) || x == '_') || name.Length < 3)
            throw new InvalidOperationException("Enter a plan code of 2–30 letters, numbers, or underscores and a plan name.");
        if (existing is not null && existing.Code != code) throw new InvalidOperationException("A plan code cannot be changed after creation.");
        if (maxBranches < 1 || maxBranches > 100) throw new InvalidOperationException("Branch limit must be between 1 and 100.");
        if (code is "MAIN" or "PROMO") features |= SubscriptionFeatures.Reports;
        if (features.HasFlag(SubscriptionFeatures.Appointments) && !features.HasFlag(SubscriptionFeatures.Clients) ||
            features.HasFlag(SubscriptionFeatures.Clinical) && !features.HasFlag(SubscriptionFeatures.Appointments) ||
            (features.HasFlag(SubscriptionFeatures.Reminders) || features.HasFlag(SubscriptionFeatures.Promotions)) && !features.HasFlag(SubscriptionFeatures.Clients) ||
            features.HasFlag(SubscriptionFeatures.Satisfaction) && !features.HasFlag(SubscriptionFeatures.Collection) ||
            features.HasFlag(SubscriptionFeatures.Reports) &&
                (!features.HasFlag(SubscriptionFeatures.Collection) || !features.HasFlag(SubscriptionFeatures.Clients) || !features.HasFlag(SubscriptionFeatures.Appointments)) ||
            features.HasFlag(SubscriptionFeatures.IncrementalSync) && !features.HasFlag(SubscriptionFeatures.Backup))
            throw new InvalidOperationException("Appointments require clients; clinical care requires appointments; reminders and promotions require clients; satisfaction requires collection; reports require collection, clients, and appointments; incremental sync requires backup.");
        if (!features.HasFlag(SubscriptionFeatures.Branching)) maxBranches = 1;
        if (GetTenants().Where(x => x.SubscriptionPlanCode == code).Any(x => GetBranches(x.Id).Count(b => b.IsActive) > maxBranches))
            throw new InvalidOperationException("Deactivate extra branches before reducing this plan's branch limit.");
        if (!active && GetTenants().Any(x => x.SubscriptionPlanCode == code && x.IsActive))
            throw new InvalidOperationException("Reassign active organizations before deactivating this plan.");
        using var connection = Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = existing is null
            ? "INSERT INTO dbo.SubscriptionPlans(Code,Name,Features,MaxBranches,IsActive,UpdatedUtc) VALUES(@code,@name,@features,@branches,@active,@now);"
            : "UPDATE dbo.SubscriptionPlans SET Name=@name,Features=@features,MaxBranches=@branches,IsActive=@active,UpdatedUtc=@now WHERE Code=@code;";
        command.Parameters.AddWithValue("@code", code); command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@features", (int)features); command.Parameters.AddWithValue("@branches", maxBranches);
        command.Parameters.AddWithValue("@active", active); command.Parameters.AddWithValue("@now", DateTime.UtcNow);
        command.ExecuteNonQuery();
        AddAudit(connection, transaction, actor, "", existing is null ? "Create subscription" : "Update subscription", "SubscriptionPlan", code, $"{name} · {features} · {maxBranches} branches");
        transaction.Commit();
    }

    private void EnsureActorActive(UserSession actor)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*) FROM dbo.Users u
            JOIN dbo.UserTenantRoles r ON r.UserId=u.Id
            LEFT JOIN dbo.Tenants t ON t.Id=r.TenantId
            WHERE u.Id=@id AND u.IsActive=1 AND r.Role=@role
              AND ((r.TenantId=@tenant AND t.IsActive=1) OR (r.TenantId IS NULL AND r.Role='SuperAdmin'));
            """;
        command.Parameters.AddWithValue("@id", actor.UserId); command.Parameters.AddWithValue("@role", actor.Role.ToString());
        command.Parameters.AddWithValue("@tenant", actor.TenantId);
        if (Convert.ToInt32(command.ExecuteScalar()) != 1)
            throw new UnauthorizedAccessException("Your account or organization is no longer active. Sign in again.");
    }

    public IReadOnlyList<AuditLogRecord> GetAuditLogs(string? tenantId = null, int limit = 250)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TOP (@limit) Id,OccurredUtc,ActorUsername,TenantId,Action,EntityType,EntityId,Details
            FROM dbo.MasterAuditLog
            WHERE (@tenant='' OR TenantId=@tenant)
            ORDER BY Id DESC;
            """;
        command.Parameters.AddWithValue("@tenant", tenantId ?? string.Empty);
        command.Parameters.AddWithValue("@limit", limit);
        using var reader = command.ExecuteReader();
        var records = new List<AuditLogRecord>();
        while (reader.Read())
        {
            records.Add(new AuditLogRecord
            {
                Id = reader.GetInt64(0), OccurredUtc = DateTime.SpecifyKind(reader.GetDateTime(1), DateTimeKind.Utc),
                ActorUsername = reader.GetString(2), TenantId = reader.GetString(3), Action = reader.GetString(4),
                EntityType = reader.GetString(5), EntityId = reader.GetString(6), Details = reader.GetString(7)
            });
        }
        return records;
    }

    private void SaveAccount(UserSession actor, StaffAccountRecord? existing, string tenantId, string username, string fullName, UserRole role, string? password)
    {
        username = username.Trim();
        fullName = fullName.Trim();
        if (username.Length < 3 || fullName.Length < 2)
            throw new InvalidOperationException("Enter a username of at least 3 characters and the staff member’s full name.");
        if (existing is null && string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("A temporary password is required for a new account.");

        using var connection = Open();
        using var transaction = connection.BeginTransaction(IsolationLevel.Serializable);
        var userId = existing?.UserId ?? Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        if (existing is null)
        {
            var salt = RandomNumberGenerator.GetBytes(32);
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO dbo.Users(Id,Username,NormalizedUsername,PasswordHash,PasswordSalt,FullName,Initials,IsActive,Version,CreatedUtc,UpdatedUtc)
                VALUES(@id,@username,@normalized,@hash,@salt,@name,@initials,1,1,@now,@now);
                INSERT INTO dbo.UserTenantRoles(UserId,TenantId,Role,IsPrimary,Version) VALUES(@id,@tenant,@role,1,1);
                """;
            AddAccountParameters(insert, userId, username, fullName, role, tenantId, now);
            insert.Parameters.AddWithValue("@hash", HashPassword(password!, salt));
            insert.Parameters.AddWithValue("@salt", salt);
            insert.ExecuteNonQuery();
        }
        else
        {
            using var update = connection.CreateCommand();
            update.Transaction = transaction;
            update.CommandText = """
                UPDATE dbo.Users SET Username=@username,NormalizedUsername=@normalized,FullName=@name,Initials=@initials,
                    Version=Version+1,UpdatedUtc=@now
                WHERE Id=@id AND Version=@expected;
                """;
            AddAccountParameters(update, userId, username, fullName, role, tenantId, now);
            update.Parameters.AddWithValue("@expected", existing.Version);
            if (update.ExecuteNonQuery() != 1)
                throw new DataConcurrencyException("This account was changed by another administrator. Reload and try again.");

            using var roleUpdate = connection.CreateCommand();
            roleUpdate.Transaction = transaction;
            roleUpdate.CommandText = "DELETE FROM dbo.UserTenantRoles WHERE UserId=@id; INSERT INTO dbo.UserTenantRoles(UserId,TenantId,Role,IsPrimary,Version) VALUES(@id,@tenant,@role,1,1);";
            roleUpdate.Parameters.AddWithValue("@id", userId);
            roleUpdate.Parameters.AddWithValue("@tenant", tenantId);
            roleUpdate.Parameters.AddWithValue("@role", role.ToString());
            roleUpdate.ExecuteNonQuery();

            if (!string.IsNullOrWhiteSpace(password))
            {
                var salt = RandomNumberGenerator.GetBytes(32);
                using var passwordUpdate = connection.CreateCommand();
                passwordUpdate.Transaction = transaction;
                passwordUpdate.CommandText = "UPDATE dbo.Users SET PasswordHash=@hash,PasswordSalt=@salt WHERE Id=@id;";
                passwordUpdate.Parameters.AddWithValue("@hash", HashPassword(password, salt));
                passwordUpdate.Parameters.AddWithValue("@salt", salt);
                passwordUpdate.Parameters.AddWithValue("@id", userId);
                passwordUpdate.ExecuteNonQuery();
            }
        }

        AddAudit(connection, transaction, actor, tenantId, existing is null ? "Create account" : "Update account", "User", userId, $"{username} · {role}");
        transaction.Commit();
    }

    private static void AddAccountParameters(SqlCommand command, string userId, string username, string fullName, UserRole role, string tenantId, DateTime now)
    {
        command.Parameters.AddWithValue("@id", userId);
        command.Parameters.AddWithValue("@username", username);
        command.Parameters.AddWithValue("@normalized", NormalizeUsername(username));
        command.Parameters.AddWithValue("@name", fullName);
        command.Parameters.AddWithValue("@initials", Initials(fullName));
        command.Parameters.AddWithValue("@now", now);
        command.Parameters.AddWithValue("@tenant", tenantId);
        command.Parameters.AddWithValue("@role", role.ToString());
    }

    private void SeedTenant(SqlConnection connection, SqlTransaction transaction, string id, string code, string name, string databaseName)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO dbo.Tenants(Id,Code,Name,DatabasePath,SyncEndpoint,IsActive,CreatedUtc,SubscriptionPlanCode) VALUES(@id,@code,@name,@path,@sync,1,@created,@plan);";
        command.Parameters.AddWithValue("@id", id); command.Parameters.AddWithValue("@code", code); command.Parameters.AddWithValue("@name", name);
        command.Parameters.AddWithValue("@path", databaseName); command.Parameters.AddWithValue("@sync", $"cloud-sync://{id}"); command.Parameters.AddWithValue("@created", DateTime.UtcNow);
        command.Parameters.AddWithValue("@plan", id switch { "tenant-a" => "MAIN", "tenant-b" => "PROMO", _ => "FULL" });
        command.ExecuteNonQuery();
        using var branch = connection.CreateCommand();
        branch.Transaction = transaction;
        branch.CommandText = "INSERT INTO dbo.Branches(Id,TenantId,Code,Name,DatabasePath,IsPrimary,IsActive) VALUES(@branch,@tenant,'MAIN',@name,@database,1,1);";
        branch.Parameters.AddWithValue("@branch", id + "-main"); branch.Parameters.AddWithValue("@tenant", id);
        branch.Parameters.AddWithValue("@name", name); branch.Parameters.AddWithValue("@database", databaseName);
        branch.ExecuteNonQuery();
    }

    private static bool TenantExists(SqlConnection connection, SqlTransaction transaction, string tenantId)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM dbo.Tenants WITH (UPDLOCK,HOLDLOCK) WHERE Id=@id;";
        command.Parameters.AddWithValue("@id", tenantId);
        return Convert.ToInt32(command.ExecuteScalar()) != 0;
    }

    private static void SeedUser(SqlConnection connection, SqlTransaction transaction, string username, string fullName, string initials, UserRole role, string? tenantId)
    {
        var userId = Guid.NewGuid().ToString("N");
        var salt = RandomNumberGenerator.GetBytes(32);
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO dbo.Users(Id,Username,NormalizedUsername,PasswordHash,PasswordSalt,FullName,Initials,IsActive,Version,CreatedUtc,UpdatedUtc)
            VALUES(@id,@username,@normalized,@hash,@salt,@name,@initials,1,1,@now,@now);
            INSERT INTO dbo.UserTenantRoles(UserId,TenantId,Role,IsPrimary,Version) VALUES(@id,@tenant,@role,1,1);
            """;
        command.Parameters.AddWithValue("@id", userId); command.Parameters.AddWithValue("@username", username);
        command.Parameters.AddWithValue("@normalized", NormalizeUsername(username)); command.Parameters.AddWithValue("@hash", HashPassword("pawcare123", salt));
        command.Parameters.AddWithValue("@salt", salt); command.Parameters.AddWithValue("@name", fullName); command.Parameters.AddWithValue("@initials", initials);
        command.Parameters.AddWithValue("@now", DateTime.UtcNow); command.Parameters.AddWithValue("@tenant", (object?)tenantId ?? DBNull.Value);
        command.Parameters.AddWithValue("@role", role.ToString()); command.ExecuteNonQuery();
    }

    private static void SeedUserIfMissing(SqlConnection connection, SqlTransaction transaction, string username, string fullName, string initials, UserRole role, string? tenantId)
    {
        using var check = connection.CreateCommand();
        check.Transaction = transaction;
        check.CommandText = "SELECT COUNT(*) FROM dbo.Users WITH (UPDLOCK,HOLDLOCK) WHERE NormalizedUsername=@username;";
        check.Parameters.AddWithValue("@username", NormalizeUsername(username));
        if (Convert.ToInt32(check.ExecuteScalar()) == 0)
            SeedUser(connection, transaction, username, fullName, initials, role, tenantId);
    }

    private static void AddAudit(SqlConnection connection, SqlTransaction transaction, UserSession actor, string tenantId, string action, string entityType, string entityId, string details)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO dbo.MasterAuditLog(OccurredUtc,ActorUserId,ActorUsername,TenantId,Action,EntityType,EntityId,Details) VALUES(@time,@actor,@name,@tenant,@action,@type,@entity,@details);";
        command.Parameters.AddWithValue("@time", DateTime.UtcNow); command.Parameters.AddWithValue("@actor", actor.UserId);
        command.Parameters.AddWithValue("@name", actor.Name); command.Parameters.AddWithValue("@tenant", tenantId); command.Parameters.AddWithValue("@action", action);
        command.Parameters.AddWithValue("@type", entityType); command.Parameters.AddWithValue("@entity", entityId); command.Parameters.AddWithValue("@details", details);
        command.ExecuteNonQuery();
    }

    private void EnsureDatabase(string databaseName)
    {
        if (!databaseName.All(x => char.IsLetterOrDigit(x) || x == '_'))
            throw new InvalidOperationException("Database names may contain only letters, numbers, and underscores.");
        using var connection = new SqlConnection(_paths.ServerConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandTimeout = 120;
        if (_paths.ServerName.StartsWith(@"(localdb)\", StringComparison.OrdinalIgnoreCase))
        {
            var directory = Path.Combine(_paths.Root, "sql-databases");
            Directory.CreateDirectory(directory);
            var dataFile = Path.Combine(directory, databaseName + ".mdf").Replace("'", "''");
            var logFile = Path.Combine(directory, databaseName + "_log.ldf").Replace("'", "''");
            command.CommandText = $"""
                IF DB_ID(@name) IS NULL
                    CREATE DATABASE [{databaseName}]
                    ON PRIMARY (NAME=N'{databaseName}', FILENAME=N'{dataFile}')
                    LOG ON (NAME=N'{databaseName}_log', FILENAME=N'{logFile}');
                """;
        }
        else command.CommandText = $"IF DB_ID(@name) IS NULL CREATE DATABASE [{databaseName}];";
        command.CommandText += $" ALTER DATABASE [{databaseName}] SET AUTO_CLOSE OFF; ALTER DATABASE [{databaseName}] SET AUTO_SHRINK OFF;";
        command.Parameters.AddWithValue("@name", databaseName);
        command.ExecuteNonQuery();
    }

    private SqlConnection Open()
    {
        var connection = new SqlConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static void Execute(SqlConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private static byte[] HashPassword(string password, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA512, 64);

    private static string NormalizeUsername(string username) => username.Trim().ToUpperInvariant();
    private static string Initials(string fullName) => string.Concat(fullName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(x => char.ToUpperInvariant(x[0])));
    private static string RoleTitle(UserRole role) => role switch
    {
        UserRole.Manager => "Clinic Manager",
        UserRole.Veterinarian => "Veterinarian",
        UserRole.Admin => "Branch Administrator",
        UserRole.SuperAdmin => "Super Administrator",
        _ => "Receptionist"
    };
}
