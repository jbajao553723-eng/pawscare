# PawCare CRM API setup

The professor's Company and CompanyDatabase pattern is implemented in the PawCare API. `MasterCRMJ` is the EF Core directory for companies, registered devices, and each company's active tenant database. The desktop app also maintains its own `PawCareMaster` per server for PawCare user accounts, roles, subscriptions, and branches.

`MasterCRMJ` normalizes the earlier `MAsterCRMJ` capitalization typo only. The professor-required `Company`, `CompanyDatabase`, `Device`, `MasterErpDbContext`, and tenant entity patterns remain unchanged, so the naming cleanup does not replace the required design.

| Professor's sample | PawCare use |
|---|---|
| Company | Veterinary clinic / tenant |
| Device | Registered clinic computer or workstation |
| Customer | Pet owner / CRM client |
| Product | Medicine, vaccine, treatment item, or service |
| Supplier | Veterinary supplier |
| Inventory | Quantity and reorder level for a product |

The EF tenant tables are `Customers`, `Products`, `Suppliers`, and `Inventories`. They coexist with the desktop app's existing operational tables. Records created through one interface are not automatically copied into the other interface yet.

## Local configuration

The API uses Windows authentication. The central `MasterCRMJ` directory is on `.\SQLEXPRESS`; active registrations route Tenant A to `PawCareTenantA` on Server 1, Tenant B to `PawCareTenantB` on Server 2, and Tenant C to `PawCareTenantC` on Server 3. The `LocalWindows` credential key selects integrated security; no SQL password is stored in the repository.

From the repository root:

```powershell
dotnet tool restore
powershell -ExecutionPolicy Bypass -File scripts/configure-crm-api.ps1
dotnet run --project src/PawCare.Api
```

`configure-crm-api.ps1` delegates to `configure-login-routing.ps1`. It migrates the central directory and each tenant database on its assigned server, initializes each server's `PawCareMaster`, and seeds the globally unique A/B/C usernames used by the selector-free desktop login.

With the API running in Development, send requests from `src/PawCare.Api/PawCare.Api.http`. `GET /test-tenant/1` checks the resolver and factory; company IDs 1, 2, and 3 are the existing local tenants. Use `GET /tenant/1/products`, `/customers`, `/suppliers`, and `/inventories` to inspect separate tenant tables.

The demonstration POST endpoints are available only in Development. Add authentication and authorization before enabling write routes on a network host. To point a tenant at another SQL host, register that host and database in `CompanyDatabases`, set its `CredentialKey`, and configure `TenantCredentials:<key>` through environment variables or a secrets store. Apply the tenant migration to that database first.
