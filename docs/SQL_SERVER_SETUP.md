# SQL Server and SSMS Setup

## Installed configuration on this computer

- SQL Server instance: `.\SQLEXPRESS`
- Authentication: Windows Authentication
- Provider: `Microsoft.Data.SqlClient` 7.1.1
- Configuration file: `src/PawCare.Desktop/appsettings.json`
- Server 1 databases: `MasterCRMJ`, `PawCareMaster`, `PawCareTenantA`

| Profile | Instance | Database files and sync mirror |
|---|---|---|
| PawCare Server 1 | `.\SQLEXPRESS` | `MasterCRMJ`, `PawCareMaster`, `PawCareTenantA` |
| PawCare Server 2 | `(localdb)\PawCareServer2` | `PawCareMaster`, `PawCareTenantB` |
| PawCare Server 3 | `(localdb)\PawCareServer3` | `PawCareMaster`, `PawCareTenantC` |

Each profile has its own `PawCareMaster` and only its assigned tenant database. Server 2 and Server 3 are separate LocalDB SQL processes on this computer, rather than network servers. LocalDB starts on demand under the signed-in Windows user. The sign-in screen accepts only a username and password. `MasterCRMJ.dbo.LoginRoutes` maps each globally unique username to its company, tenant, and active `CompanyDatabases` server registration. Super Admin can change the administration target from the internal **Server settings** page; staff never select a server during sign-in.

## Open the databases in SSMS

1. Open SQL Server Management Studio.
2. For **Server name**, enter `.\SQLEXPRESS`.
3. Select **Windows Authentication**.
4. Select **Connect**.
5. In Object Explorer, right-click **Databases** and select **Refresh**.
6. Expand `PawCareMaster` or any `PawCareTenant*` database, then expand **Tables**.

The login accounts used by PawCare are application accounts stored in `PawCareMaster.dbo.Users`; they are not SQL Server logins. Their passwords are salted PBKDF2 hashes.

To initialize all three server/database sets, apply the EF migrations, seed the routed demo accounts, and configure the central username directory, run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/configure-login-routing.ps1
```

## Configure another Visual Studio computer

Install these components first:

- Visual Studio with the **.NET desktop development** workload
- .NET 10 SDK
- SQL Server 2025 Express or Developer edition
- SQL Server Management Studio
- SQL Server command-line utilities (`sqlcmd`)

From the repository root, run:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/configure-sqlserver.ps1
```

The script verifies SQL connectivity, initializes that profile's `PawCareMaster` and assigned tenant database, then writes the profile to `appsettings.json` only after verification succeeds. It grants the local SQL Server service account access to the backup destination when configuring `.\SQLEXPRESS`.

To recreate the two additional local development instances on this computer:

```powershell
SqlLocalDB create PawCareServer2
SqlLocalDB create PawCareServer3
powershell -ExecutionPolicy Bypass -File scripts/configure-sqlserver.ps1 -ProfileName "PawCare Server 2" -ServerInstance "(localdb)\PawCareServer2" -TenantId "tenant-b" -MaxServerMemoryMb 512
powershell -ExecutionPolicy Bypass -File scripts/configure-sqlserver.ps1 -ProfileName "PawCare Server 3" -ServerInstance "(localdb)\PawCareServer3" -TenantId "tenant-c" -MaxServerMemoryMb 512
```

`SqlLocalDB info` lists the local instances. Connect to each instance in SSMS using its value from the table and Windows Authentication. The console initializer can also be run directly with `dotnet run --project src/PawCare.DatabaseSetup` and `PAWCARE_SQL_SERVER` set to the target instance.

If `.\SQLEXPRESS` reports SQL errors `17300` or `701` and cannot complete logins, close unused applications and stop idle LocalDB instances. Restart the `MSSQL$SQLEXPRESS` Windows service from an **Administrator** PowerShell window, then rerun the console initializer. A normal user session cannot restart that service:

```powershell
SqlLocalDB stop PawCareServer2
SqlLocalDB stop PawCareServer3
Restart-Service -Name 'MSSQL$SQLEXPRESS'
dotnet run --project src/PawCare.DatabaseSetup/PawCare.DatabaseSetup.csproj
```

To add another independent SQL Server profile, run the script again with a different profile name and instance:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/configure-sqlserver.ps1 `
  -ProfileName "PawCare Remote Server 4" `
  -ServerInstance "PAWCARE04\SQLEXPRESS" `
  -TenantId "tenant-c" `
  -DatabasePrefix "PawCare" `
  -MirrorDirectory "\\FILEHOST\PawCareSync\Server4"
```

The remote SQL Server service account and the desktop user both need access to the mirror share for full backup and sync. The script cannot grant those remote permissions automatically. Each instance needs database-creation rights during setup. Do not reuse the same profile name for a different instance unless you intend to replace that profile.

The first three profiles are installed local instances. `PAWCARE04` and its network share above are examples for a future remote server; they must exist before adding that profile. The subscription tier is assigned to each company by Super Admin and is independent of the server's name.

To target a different local instance or database prefix:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/configure-sqlserver.ps1 `
  -ProfileName "PawCare Dev" `
  -ServerInstance ".\MSSQLSERVER2025" `
  -TenantId "tenant-a" `
  -DatabasePrefix "PawCareDev"
```

## Connection configuration

Configured `appsettings.json`:

```json
{
  "Database": {
    "Server": ".\\SQLEXPRESS",
    "DatabasePrefix": "PawCare",
    "TrustServerCertificate": true,
    "Servers": [
      { "Name": "PawCare Server 1", "Instance": ".\\SQLEXPRESS", "DatabasePrefix": "PawCare", "TenantId": "tenant-a" },
      { "Name": "PawCare Server 2", "Instance": "(localdb)\\PawCareServer2", "DatabasePrefix": "PawCare", "TenantId": "tenant-b", "MirrorDirectory": "%LOCALAPPDATA%\\PawCare\\data\\server-mirrors\\pawcare-server-2" },
      { "Name": "PawCare Server 3", "Instance": "(localdb)\\PawCareServer3", "DatabasePrefix": "PawCare", "TenantId": "tenant-c", "MirrorDirectory": "%LOCALAPPDATA%\\PawCare\\data\\server-mirrors\\pawcare-server-3" }
    ]
  }
}
```

The desktop application uses integrated Windows security and encrypted SQL client connections. `TrustServerCertificate` is enabled for this local development instance because SQL Server Express normally has a self-signed development certificate. Use a trusted server certificate and set it to `false` for production.

Environment overrides:

```powershell
$env:PAWCARE_SQL_SERVER = ".\SQLEXPRESS"
$env:PAWCARE_DATABASE_PREFIX = "PawCare"
$env:PAWCARE_MIRROR_DIRECTORY = "\\FILEHOST\PawCareSync\Server2"
```

## Useful SSMS verification queries

Run against `PawCareMaster`:

```sql
SELECT Id, Code, Name, DatabasePath, IsActive
FROM dbo.Tenants
ORDER BY Code;

SELECT u.Username, u.FullName, r.Role, r.TenantId, u.IsActive
FROM dbo.Users AS u
JOIN dbo.UserTenantRoles AS r ON r.UserId = u.Id
ORDER BY r.TenantId, r.Role, u.Username;

SELECT t.Code, t.Name, t.SubscriptionPlanCode, p.Features, p.MaxBranches
FROM dbo.Tenants AS t
JOIN dbo.SubscriptionPlans AS p ON p.Code = t.SubscriptionPlanCode
ORDER BY t.Code;

SELECT t.Code AS CompanyCode, b.Code AS BranchCode, b.Name, b.DatabasePath, b.IsActive
FROM dbo.Branches AS b
JOIN dbo.Tenants AS t ON t.Id = b.TenantId
ORDER BY t.Code, b.Code;
```

Run against a tenant database:

```sql
SELECT COUNT(*) AS ActiveInquiries FROM dbo.Inquiries WHERE IsDeleted = 0;
SELECT COUNT(*) AS ActiveComplaints FROM dbo.Complaints WHERE IsDeleted = 0;
SELECT COUNT(*) AS ActiveFeedback FROM dbo.Feedback WHERE IsDeleted = 0;
SELECT COUNT(*) AS PendingSyncEvents FROM dbo.OutboxEvents WHERE SyncState = 'Pending';
```

## Production notes

Do not use `sa` credentials in the application. Provision a restricted Windows/service identity with access only to `PawCareMaster` and the required tenant databases. Back up encryption keys, restrict backup-folder ACLs, configure retention, and test `.bak` restoration regularly.
