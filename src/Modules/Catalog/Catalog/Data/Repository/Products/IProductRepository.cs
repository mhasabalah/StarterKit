namespace Catalog.Data.Repository.Products;

/// <summary>
/// Write-side repository for the <see cref="Product"/> aggregate. Used by command handlers.
/// Query handlers read through <see cref="CatalogDbContext"/> directly with projections.
/// </summary>
public interface IProductRepository
{
    Task<Product?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>True when another product already uses <paramref name="normalizedSku"/>.</summary>
    Task<bool> SkuExistsAsync(
        string normalizedSku,
        Guid? excludeProductId = null,
        CancellationToken cancellationToken = default);

    Task AddAsync(Product product, CancellationToken cancellationToken = default);
    Task UpdateAsync(Product product, CancellationToken cancellationToken = default);
    Task DeleteAsync(Product product, CancellationToken cancellationToken = default);
}
