namespace Catalog.Domains.Products.Services;

/// <summary>
/// The shared skeleton of every workflow command (load → authorize → expect version → transition →
/// save → return the new version), so the seven workflow handlers stay one-liners and cannot drift.
/// </summary>
public class ProductWorkflowRunner(IProductRepository repository, CatalogDbContext dbContext)
{
    public async Task<ProductStatusChangedResult> RunAsync(
        Guid productId,
        long? expectedVersion,
        Action<Product> authorize,
        Action<Product> transition,
        CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(productId, cancellationToken)
                      ?? throw new ProductNotFoundException(productId);

        authorize(product);

        // Optimistic concurrency: a stale client version makes SaveChanges fail → 409.
        dbContext.ExpectVersion(product, expectedVersion);

        transition(product);
        await repository.SaveChangesAsync(cancellationToken);

        return new ProductStatusChangedResult(product.Id, product.Status, product.Version);
    }
}
