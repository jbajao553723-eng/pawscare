namespace PawCare.Infrastructure.Services;

public sealed record TenantDatabaseInfo(string ServerName, string DatabaseName, string CredentialKey);

public interface ITenantDatabaseResolver
{
    Task<TenantDatabaseInfo> GetDatabaseInfoAsync(int companyId, CancellationToken cancellationToken = default);
}
