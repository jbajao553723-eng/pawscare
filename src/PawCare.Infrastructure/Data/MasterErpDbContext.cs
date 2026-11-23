using PawCare.Domain.Entities;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace PawCare.Infrastructure.Data;

public class MasterErpDbContext : IdentityDbContext
{
    public MasterErpDbContext(DbContextOptions<MasterErpDbContext> options) : base(options)
    {
    }

    public DbSet<Company> Companies => Set<Company>();
    public DbSet<CompanyDatabase> CompanyDatabases => Set<CompanyDatabase>();
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<LoginRoute> LoginRoutes => Set<LoginRoute>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Company>(entity =>
        {
            entity.HasKey(company => company.CompanyId);
            entity.Property(company => company.CompanyCode).HasMaxLength(50).IsRequired();
            entity.Property(company => company.CompanyName).HasMaxLength(200).IsRequired();
            entity.HasIndex(company => company.CompanyCode).IsUnique();
        });

        builder.Entity<CompanyDatabase>(entity =>
        {
            entity.HasKey(database => database.CompanyDatabaseId);
            entity.Property(database => database.ServerName).HasMaxLength(200).IsRequired();
            entity.Property(database => database.DatabaseName).HasMaxLength(200).IsRequired();
            entity.Property(database => database.CredentialKey).HasMaxLength(100).HasDefaultValue("LocalWindows").IsRequired();
            entity.HasIndex(database => new { database.ServerName, database.DatabaseName }).IsUnique();
            entity.HasOne(database => database.Company)
                .WithMany()
                .HasForeignKey(database => database.CompanyId)
                .OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<Device>(entity =>
        {
            entity.HasKey(device => device.DeviceId);
            entity.Property(device => device.DeviceCode).HasMaxLength(50).IsRequired();
            entity.Property(device => device.DeviceName).HasMaxLength(200).IsRequired();
            entity.HasOne(device => device.Company).WithMany(company => company.Devices)
                .HasForeignKey(device => device.CompanyId).OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(device => new { device.CompanyId, device.DeviceCode }).IsUnique();
        });
        builder.Entity<LoginRoute>(entity =>
        {
            entity.HasKey(route => route.LoginRouteId);
            entity.Property(route => route.Username).HasMaxLength(100).IsRequired();
            entity.Property(route => route.NormalizedUsername).HasMaxLength(100).IsRequired();
            entity.Property(route => route.TenantId).HasMaxLength(50).IsRequired();
            entity.HasIndex(route => route.NormalizedUsername).IsUnique();
            entity.HasOne(route => route.Company).WithMany(company => company.LoginRoutes)
                .HasForeignKey(route => route.CompanyId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
