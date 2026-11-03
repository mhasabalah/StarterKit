using Shared.Core.MultiTenancy.Data;
using Catalog.Data.Configurations;

namespace Catalog.Data.Repository;

/// <summary>
/// Write-side repository for the <see cref="Product"/> aggregate (used by command handlers).
/// Query handlers read through <see cref="CatalogDbContext"/> directly with projections.
/// Loaded aggregates are tracked: mutate them, then call <see cref="SaveChangesAsync"/>.
/// </summary>
public interface IProductRepository
{
    /// <summary>Loads the aggregate with its gallery, attributes and tags. Soft-deleted products are invisible.</summary>
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Same, but also finds soft-deleted products (restore / purge).</summary>
    Task<Product?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>True when another product (soft-deleted ones included) already uses <paramref name="normalizedSku"/>.</summary>
    Task<bool> SkuExistsAsync(string normalizedSku, Guid? excludeProductId = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>Saves tracked changes; a concurrent duplicate SKU becomes <see cref="ProductSkuAlreadyExistsException"/>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Physically deletes the row (children cascade in the database).</summary>
    Task HardDeleteAsync(Product product, CancellationToken cancellationToken = default);
}

public class ProductRepository(CatalogDbContext context) : IProductRepository
{
    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithChildren(context.Products).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<Product?> GetByIdIncludingDeletedAsync(Guid id, CancellationToken cancellationToken = default) =>
        WithChildren(context.Products.IgnoreNonIsolationFilters()).FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<bool> SkuExistsAsync(
        string normalizedSku, Guid? excludeProductId = null, CancellationToken cancellationToken = default) =>
        context.Products
            .IgnoreNonIsolationFilters()
            .AsNoTracking()
            .AnyAsync(p => p.Sku == normalizedSku && (excludeProductId == null || p.Id != excludeProductId),
                cancellationToken);

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        await context.Products.AddAsync(product, cancellationToken);
        await SaveChangesAsync(cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(ProductConfiguration.SkuIndexName))
        {
            var sku = context.ChangeTracker.Entries<Product>()
                .Select(e => e.Entity.Sku)
                .FirstOrDefault() ?? string.Empty;
            throw new ProductSkuAlreadyExistsException(sku);
        }
    }

    public async Task HardDeleteAsync(Product product, CancellationToken cancellationToken = default)
    {
        context.Products.Remove(product);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static IQueryable<Product> WithChildren(IQueryable<Product> products) =>
        products
            .Include(p => p.Images)
            .Include(p => p.Attributes)
            .Include(p => p.Tags)
            .AsSplitQuery();
}
