using PawCare.Infrastructure.Data;
using PawCare.Infrastructure.Services;
using PawCare.Domain.Entities;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("MasterErp")
    ?? throw new InvalidOperationException("Connection string 'MasterErp' is missing.");

builder.Services.AddDbContext<MasterErpDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddDbContext<TenantErpDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("TenantErp")));
builder.Services.AddScoped<ITenantDatabaseResolver, TenantDatabaseResolver>();
builder.Services.AddScoped<ITenantDbContextFactory, TenantDbContextFactory>();
builder.Services.AddOpenApi();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health", () => TypedResults.Ok(new { status = "ok" }));

app.MapGet("/api/branches", async (MasterErpDbContext db, CancellationToken cancellationToken) =>
    await db.Companies
        .AsNoTracking()
        .Where(company => company.IsActive && db.CompanyDatabases.Any(database =>
            database.CompanyId == company.CompanyId && database.IsActive))
        .OrderBy(company => company.CompanyCode)
        .Select(company => new { Code = company.CompanyCode, Name = company.CompanyName })
        .ToListAsync(cancellationToken));

// The demonstration write routes are local development endpoints until API authentication is configured.
if (app.Environment.IsDevelopment())
{
    app.MapPost("/companies", async (Company company, MasterErpDbContext db) =>
    {
        if (string.IsNullOrWhiteSpace(company.CompanyCode) || string.IsNullOrWhiteSpace(company.CompanyName))
            return Results.BadRequest("Company code and name are required.");
        db.Companies.Add(company);
        await db.SaveChangesAsync();
        return Results.Created($"/companies/{company.CompanyId}", company);
    });
    app.MapPost("/devices", async (Device device, MasterErpDbContext db) =>
    {
        if (!await db.Companies.AnyAsync(x => x.CompanyId == device.CompanyId))
            return Results.BadRequest("Company does not exist.");
        db.Devices.Add(device);
        await db.SaveChangesAsync();
        return Results.Created($"/devices/{device.DeviceId}", device);
    });
    app.MapPost("/company-databases", async (CompanyDatabase registration, MasterErpDbContext db) =>
    {
        if (!await db.Companies.AnyAsync(x => x.CompanyId == registration.CompanyId))
            return Results.BadRequest("Company does not exist.");
        if (string.IsNullOrWhiteSpace(registration.CredentialKey))
            return Results.BadRequest("Credential key is required.");
        db.CompanyDatabases.Add(registration);
        await db.SaveChangesAsync();
        return Results.Created($"/company-databases/{registration.CompanyDatabaseId}", registration);
    });
    app.MapGet("/test-tenant/{companyId:int}", async (int companyId, ITenantDbContextFactory factory) =>
    {
        await using var tenant = await factory.CreateAsync(companyId);
        var productCount = await tenant.Products.CountAsync();
        return Results.Ok(new { companyId, canConnect = true, productCount });
    });
    app.MapPost("/tenant/{companyId:int}/products", async (int companyId, Product product, ITenantDbContextFactory factory) =>
    {
        await using var db = await factory.CreateAsync(companyId);
        db.Products.Add(product);
        await db.SaveChangesAsync();
        return Results.Created($"/tenant/{companyId}/products/{product.ProductId}", product);
    });
    app.MapGet("/tenant/{companyId:int}/products", async (int companyId, ITenantDbContextFactory factory) =>
    {
        await using var db = await factory.CreateAsync(companyId);
        return Results.Ok(await db.Products.AsNoTracking().OrderBy(x => x.ProductId).ToListAsync());
    });
    app.MapPost("/tenant/{companyId:int}/customers", async (int companyId, Customer customer, ITenantDbContextFactory factory) =>
    {
        await using var db = await factory.CreateAsync(companyId);
        db.Customers.Add(customer);
        await db.SaveChangesAsync();
        return Results.Created($"/tenant/{companyId}/customers/{customer.CustomerId}", customer);
    });
    app.MapGet("/tenant/{companyId:int}/customers", async (int companyId, ITenantDbContextFactory factory) =>
    {
        await using var db = await factory.CreateAsync(companyId);
        return Results.Ok(await db.Customers.AsNoTracking().OrderBy(x => x.CustomerId).ToListAsync());
    });
    app.MapPost("/tenant/{companyId:int}/suppliers", async (int companyId, Supplier supplier, ITenantDbContextFactory factory) =>
    {
        await using var db = await factory.CreateAsync(companyId);
        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync();
        return Results.Created($"/tenant/{companyId}/suppliers/{supplier.SupplierId}", supplier);
    });
    app.MapGet("/tenant/{companyId:int}/suppliers", async (int companyId, ITenantDbContextFactory factory) =>
    {
        await using var db = await factory.CreateAsync(companyId);
        return Results.Ok(await db.Suppliers.AsNoTracking().OrderBy(x => x.SupplierId).ToListAsync());
    });
    app.MapPost("/tenant/{companyId:int}/inventories", async (int companyId, Inventory inventory, ITenantDbContextFactory factory) =>
    {
        await using var db = await factory.CreateAsync(companyId);
        if (!await db.Products.AnyAsync(x => x.ProductId == inventory.ProductId))
            return Results.BadRequest("Product does not exist in this tenant.");
        db.Inventories.Add(inventory);
        await db.SaveChangesAsync();
        return Results.Created($"/tenant/{companyId}/inventories/{inventory.InventoryId}", inventory);
    });
    app.MapGet("/tenant/{companyId:int}/inventories", async (int companyId, ITenantDbContextFactory factory) =>
    {
        await using var db = await factory.CreateAsync(companyId);
        return Results.Ok(await db.Inventories.AsNoTracking().OrderBy(x => x.InventoryId).ToListAsync());
    });
}

app.Run();
