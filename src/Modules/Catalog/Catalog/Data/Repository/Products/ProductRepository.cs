using Npgsql;

namespace Catalog.Data.Repository.Products;

public class ProductRepository(CatalogDbContext context) : IProductRepository
{
    private const string UniqueViolationSqlState = "23505";

    public Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
        => context.Products.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<bool> SkuExistsAsync(
        string normalizedSku,
        Guid? excludeProductId = null,
        CancellationToken cancellationToken = default)
        => context.Products
            .AsNoTracking()
            .AnyAsync(p => p.Sku == normalizedSku &&
                           (excludeProductId == null || p.Id != excludeProductId),
                cancellationToken);

    public async Task AddAsync(Product product, CancellationToken cancellationToken = default)
    {
        await context.Products.AddAsync(product, cancellationToken);
        await SaveTranslatingUniqueViolationAsync(product, cancellationToken);
    }

    public Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
        => SaveTranslatingUniqueViolationAsync(product, cancellationToken);

    public async Task DeleteAsync(Product product, CancellationToken cancellationToken = default)
    {
        context.Products.Remove(product);
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// The handler pre-checks SKU uniqueness, but two concurrent requests can both pass that
    /// check. The unique index is the real guard — translate its violation into a 409.
    /// </summary>
    private async Task SaveTranslatingUniqueViolationAsync(
        Product product, CancellationToken cancellationToken)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: UniqueViolationSqlState })
        {
            throw new ProductSkuAlreadyExistsException(product.Sku);
        }
    }
}
