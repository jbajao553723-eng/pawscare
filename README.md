# PawCare Veterinary CRM — Windows Desktop

PawCare is a native C# WPF veterinary clinic management system connected to Microsoft SQL Server. Staff sign in with a globally unique username and password; the central `MasterCRMJ` login directory resolves the correct SQL Server and tenant automatically. Each SQL Server instance has its own `PawCareMaster` for users, subscriptions, organizations, and branches, and each branch uses an isolated operational database.

## SQL Server topology

The development environment uses one SQL Express instance with Windows authentication and a separate database for every tenant:

| SQL instance | Database set |
|---|---|
| `.\SQLEXPRESS` | `MasterCRMJ`, `PawCareMaster`, `PawCareTenantA`, `PawCareTenantB`, `PawCareTenantC` |

`MasterCRMJ` resolves each username to a tenant, `PawCareMaster` stores shared identity and subscription governance, and the three tenant databases keep operational records physically isolated. This is database-per-tenant isolation on one manageable SQL Server instance.

| Database | Purpose |
|---|---|
| `PawCareMaster` | Organizations, users, password hashes, role assignments, and master audit log |
| `PawCareTenantA` | PawCare Makati operations |
| `PawCareTenantB` | PawCare Taguig operations |
| `PawCareTenantC` | PawCare Quezon City operations |
| `MasterCRMJ` | Central EF Core company, device, database-registration, and username-routing directory on `.\SQLEXPRESS` |

Each branch database has physically separate tables for clients, appointments, inquiries, complaints, feedback, reminders, promotions, consultations, inventory, audit records, sync leases, and the transactional outbox. A subscription controls access to modules; changing plans does not erase stored records.

The seeded organizations demonstrate the plans: Tenant A uses Main Transaction (core care, collection, business intelligence, and full backup), Tenant B uses Promotional (Main plus reminders, satisfaction, offers, and campaign intelligence), and Tenant C uses Full Clinic (inventory, incremental sync, and multiple branches). Super Admin can edit plans and assign them to organizations; a tenant's identity and database do not change when its plan changes.

The clinic app initializes its account and tenant databases automatically. To configure a new development machine:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/configure-sqlserver.ps1
```

Then connect in SSMS using server name `.\SQLEXPRESS`, choose **Windows Authentication**, connect, and refresh **Databases**. See [SQL_SERVER_SETUP.md](docs/SQL_SERVER_SETUP.md).

## Demo accounts

All seeded accounts use password `pawcare123`.

| Role | Username | Organization |
|---|---|---|
| Receptionist | `receptionista` | PawCare Makati |
| Manager | `managera` | PawCare Makati |
| Veterinarian | `veterinariana` | PawCare Makati |
| Admin/Owner | `admina` | PawCare Makati |
| Admin/Owner | `adminb` | PawCare Taguig |
| Admin/Owner | `adminc` | PawCare Quezon City |
| Super Admin | `superadmin` | All organizations |

Tenant B also has `receptionistb`, `managerb`, and `veterinarianb`. Tenant C has `receptionistc`, `managerc`, and `veterinarianc`. The central login directory resolves the server from the username; staff do not choose a SQL Server at sign-in.

To add deterministic, linked demo records to an operational tenant database, run:

```powershell
dotnet run --project src/PawCare.DatabaseSetup/PawCare.DatabaseSetup.csproj -- --seed-demo-clients 200 --tenant tenant-a
```

The command creates one client, appointment, and inquiry per requested record through the normal versioning, audit, and outbox pipeline. Its stable `*-DEMO-####` identifiers make the import safe to rerun without duplicating those records.

## Local-first and race protection

- The selected SQL instance and branch database are the operational source of truth.
- Business data and its idempotent outbox event commit in one serializable transaction.
- Database revision checks reject stale sessions instead of silently overwriting newer work.
- Entity versions, unique idempotency keys, update locks, and per-tenant sync leases protect concurrent writes and sync operations.
- Incremental sync writes pending-event JSON envelopes beneath `%LOCALAPPDATA%\PawCare\data\cloud-sync-simulator`.
- Full backup creates an SSMS-restorable SQL Server `.bak` file in the same sync-only destination.
- The SQL Server service account receives write permission only on the cloud mirror directory, not the application source tree.

The previous SQLite files are no longer read by the application. They remain under local app data as a rollback artifact and were not deleted automatically.

## Visual Studio 2026

Install **.NET desktop development**, **ASP.NET and web development**, the
.NET 10 SDK, and the .NET Framework 4.7 targeting pack. Open `PawCare.slnx`
from the repository root, select **PawCare (clinic and lab)**, and press F5
to start the clinic app, Web API, and Windows Forms directory. Select
**Load branches** in the directory to check the API and SQL Server.

`src` contains the WPF clinic app, the .NET Framework 4.7 Windows Forms
directory, and the .NET 10 API, Domain, and Infrastructure projects.
The API uses `MasterErpDbContext` with Identity and SQL Server. In
Development its OpenAPI document is at `http://localhost:5147/openapi/v1.json`.

The clinic SQL settings are in `src/PawCare.Desktop/appsettings.json`.
`PAWCARE_SQL_SERVER`, `PAWCARE_DATABASE_PREFIX`, and `PAWCARE_TENANT_ID` override those settings.

In SSMS, the local **CRMJ → CRMJ** registration opens `MasterCRMJ` on
`.\SQLEXPRESS` with Windows Authentication. On a fresh local installation,
initialize the clinic databases first using the SQL Server setup above,
then apply the EF Core migrations and register the existing branches:

```powershell
dotnet tool restore
dotnet ef database update --project src/PawCare.Infrastructure --startup-project src/PawCare.Api
sqlcmd -S '.\SQLEXPRESS' -E -C -b -i scripts/register-pawcare-branches.sql
```

## Build and verify

```powershell
dotnet build PawCare.slnx -c Release
dotnet run --project tests/PawCare.SmokeTests/PawCare.SmokeTests.csproj -c Release
dotnet run --project src/PawCare.Desktop/PawCare.Desktop.csproj -c Release
```

The test project creates temporary master and tenant databases on the configured test instance, verifies routing, database-per-tenant isolation, concurrency, sync, and backup, then removes only those test-prefixed databases. Set `PAWCARE_TEST_SQL_SERVER` to use a different test instance.

See [PROCESS_FLOW.md](docs/PROCESS_FLOW.md) for the role matrix and complete data flow.

The professor-style EF Core API layer has a separate master directory and tenant CRM contexts for companies, devices, customers, products, suppliers, and inventory. See [PROF_CRM_SETUP.md](docs/PROF_CRM_SETUP.md) for the local migration command, API request examples, and how these tables relate to the existing desktop CRM.
