using Microsoft.EntityFrameworkCore;
using PawCare.Domain.Entities;

namespace PawCare.Infrastructure.Data;

public class TenantErpDbContext : DbContext
{
    public TenantErpDbContext(DbContextOptions<TenantErpDbContext> options) : base(options) { }

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Inventory> Inventories => Set<Inventory>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        builder.Entity<Product>(entity =>
        {
            entity.HasKey(x => x.ProductId);
            entity.Property(x => x.ProductCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.ProductName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.UnitPrice).HasPrecision(18, 2);
            entity.HasIndex(x => x.ProductCode).IsUnique();
        });
        builder.Entity<Customer>(entity =>
        {
            entity.HasKey(x => x.CustomerId);
            entity.Property(x => x.CustomerCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.CustomerName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ContactNumber).HasMaxLength(50);
            entity.Property(x => x.EmailAddress).HasMaxLength(200);
            entity.HasIndex(x => x.CustomerCode).IsUnique();
        });
        builder.Entity<Supplier>(entity =>
        {
            entity.HasKey(x => x.SupplierId);
            entity.Property(x => x.SupplierCode).HasMaxLength(50).IsRequired();
            entity.Property(x => x.SupplierName).HasMaxLength(200).IsRequired();
            entity.HasIndex(x => x.SupplierCode).IsUnique();
        });
        builder.Entity<Inventory>(entity =>
        {
            entity.HasKey(x => x.InventoryId);
            entity.Property(x => x.QuantityOnHand).HasPrecision(18, 2);
            entity.Property(x => x.ReorderLevel).HasPrecision(18, 2);
            entity.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => x.ProductId).IsUnique();
        });
    }
}
