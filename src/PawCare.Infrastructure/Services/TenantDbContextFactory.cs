using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PawCare.Infrastructure.Data;

namespace PawCare.Infrastructure.Services;

public sealed class TenantDbContextFactory(
    ITenantDatabaseResolver resolver,
    IConfiguration configuration) : ITenantDbContextFactory
{
    public async Task<TenantErpDbContext> CreateAsync(int companyId, CancellationToken cancellationToken = default)
    {
        var database = await resolver.GetDatabaseInfoAsync(companyId, cancellationToken);
        if (string.IsNullOrWhiteSpace(database.CredentialKey))
            throw new InvalidOperationException($"Tenant credentials are not assigned for CompanyId {companyId}.");
        var credentials = configuration.GetSection($"TenantCredentials:{database.CredentialKey}");
        if (!credentials.Exists())
            throw new InvalidOperationException($"Tenant credential key '{database.CredentialKey}' is not configured.");

        var connection = new SqlConnectionStringBuilder
        {
            DataSource = database.ServerName,
            InitialCatalog = database.DatabaseName,
            // LocalWindows may disable encryption for a localhost SQL Express lab setup.
            // Hosted credential profiles should leave this enabled.
            Encrypt = credentials.GetValue("Encrypt", true),
            TrustServerCertificate = credentials.GetValue("TrustServerCertificate", false),
            ConnectTimeout = credentials.GetValue("ConnectTimeout", 60)
        };
        if (credentials.GetValue("IntegratedSecurity", false))
            connection.IntegratedSecurity = true;
        else
        {
            connection.UserID = credentials["UserId"] ?? throw new InvalidOperationException("Tenant SQL user ID is missing.");
            connection.Password = credentials["Password"] ?? throw new InvalidOperationException("Tenant SQL password is missing.");
        }
        var options = new DbContextOptionsBuilder<TenantErpDbContext>()
            .UseSqlServer(connection.ConnectionString).Options;
        return new TenantErpDbContext(options);
    }
}
