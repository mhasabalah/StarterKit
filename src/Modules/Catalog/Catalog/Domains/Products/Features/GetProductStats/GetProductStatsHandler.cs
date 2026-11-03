namespace Catalog.Domains.Products.Features.GetProductStats;

/// <summary>
/// Cross-module contract query (no endpoint): other modules / the Api host send
/// <see cref="GetProductStatsQuery"/> through ISender. One aggregate round-trip.
/// </summary>
public class GetProductStatsHandler(CatalogDbContext dbContext)
    : IQueryHandler<GetProductStatsQuery, ProductStatsDto>
{
    public async Task<ProductStatsDto> Handle(GetProductStatsQuery query, CancellationToken cancellationToken) =>
        await dbContext.Products.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new ProductStatsDto(
                g.Count(),
                g.Count(p => p.Status == ProductStatus.PendingReview),
                g.Count(p => p.Status == ProductStatus.Published),
                g.Count(p => p.IsFeatured)))
            .FirstOrDefaultAsync(cancellationToken)
        ?? new ProductStatsDto(0, 0, 0, 0);
}
