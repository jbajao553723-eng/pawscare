using PawCare.Infrastructure.Data;

namespace PawCare.Infrastructure.Services;

public interface ITenantDbContextFactory
{
    Task<TenantErpDbContext> CreateAsync(int companyId, CancellationToken cancellationToken = default);
}
