using Shared.Core.Auditing;
using Shared.Core.Messaging.Outbox;
using Shared.Core.MultiTenancy;

namespace Catalog.Data;

/// <summary>
/// DATA PLANE context (<see cref="ITenantDataContext"/>): shared or dedicated database per tenant; every entity is
/// tenant-owned (named <c>Tenant</c> filter + RLS), products are branch-scoped.
/// </summary>
public class CatalogDbContext(DbContextOptions<CatalogDbContext> options)
    : AppDbContextBase(options), ITenantDataContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<ProductAttribute> ProductAttributes => Set<ProductAttribute>();
    public DbSet<ProductTag> ProductTags => Set<ProductTag>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Tag> Tags => Set<Tag>();
    public DbSet<AttributeType> AttributeTypes => Set<AttributeType>();
    public DbSet<WishlistItem> WishlistItems => Set<WishlistItem>();
    public DbSet<TenantAuditEntry> AuditEntries => Set<TenantAuditEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("catalog");
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        // Product uses true soft delete: every IEntity type gets a "!IsDeleted" query filter.
        // Use IgnoreNonIsolationFilters() on admin/restore/purge paths. WishlistItem (not an IEntity)
        // keeps its own filter that follows the parent product — see WishlistItemConfiguration.
        builder.FilterSoftDeletedProperties();

        // Transactional outbox for ProductPublishedIntegrationEvent & co (tenant-owned row: filter + RLS like the rest).
        builder.AddTenantOutbox();

        // Audit trail of the IAuditable aggregates (Product, Category): tenant-owned row, same transaction as the change.
        builder.AddTenantAuditTrail();

        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();

        // Last: tenant + branch filters (named, AND-ed with SoftDelete) and tenant_id indexes.
        ApplyTenantFilters(builder);
    }
}
