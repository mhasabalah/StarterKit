namespace Catalog.Domains.Products.Features.PurgeProduct;

/// <summary>
/// Internal: hard-deletes an already soft-deleted product (sent by PurgeSoftDeletedProductsJob).
/// Raises ProductPurgedEvent → attachment cleanup. A product restored in the meantime is left alone.
/// </summary>
public class PurgeProductHandler(IProductRepository repository, ILogger<PurgeProductHandler> logger)
    : ICommandHandler<PurgeProductCommand, PurgeProductResult>
{
    public async Task<PurgeProductResult> Handle(PurgeProductCommand command, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdIncludingDeletedAsync(command.Id, cancellationToken);
        if (product is null || !product.IsDeleted)
        {
            logger.LogInformation("Purge skipped for product {ProductId}: not found or not deleted", command.Id);
            return new PurgeProductResult(false);
        }

        product.MarkPurged();
        await repository.HardDeleteAsync(product, cancellationToken);

        return new PurgeProductResult(true);
    }
}
