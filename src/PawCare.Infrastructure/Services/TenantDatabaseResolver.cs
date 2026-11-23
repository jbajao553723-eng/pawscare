using Microsoft.EntityFrameworkCore;
using PawCare.Infrastructure.Data;

namespace PawCare.Infrastructure.Services;

public sealed class TenantDatabaseResolver(MasterErpDbContext masterDb) : ITenantDatabaseResolver
{
    public async Task<TenantDatabaseInfo> GetDatabaseInfoAsync(int companyId, CancellationToken cancellationToken = default)
    {
        var registrations = await masterDb.CompanyDatabases.AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.IsActive && x.Company != null && x.Company.IsActive)
            .Select(x => new TenantDatabaseInfo(x.ServerName, x.DatabaseName, x.CredentialKey))
            .Take(2).ToListAsync(cancellationToken);
        return registrations.Count switch
        {
            0 => throw new InvalidOperationException($"No active tenant database is registered for CompanyId {companyId}."),
            1 => registrations[0],
            _ => throw new InvalidOperationException($"CompanyId {companyId} has multiple active tenant databases; select one before routing.")
        };
    }
}
