using System.Runtime.CompilerServices;
using Search.Contracts.Indexing;
using Shared.Core.MultiTenancy.Data;
using static Catalog.Domains.Products.SearchIndex.ProductSearchIndex;

namespace Catalog.Domains.Products.SearchIndex;

/// <summary>
/// Where Search reads products (<see cref="ProductSearchIndex"/>): every branch of the current tenant, never
/// soft-deleted ones (the DbContext filters them), projected in SQL. The product <c>Version</c> is the document version,
/// so an older write never replaces a newer one and drift detection sees stale entries.
/// </summary>
public sealed class ProductSearchDocumentSource(CatalogDbContext dbContext) : ISearchDocumentSource
{
    private const int BatchSize = 500;

    public async Task<IReadOnlyList<SearchDocument>> GetAsync(IReadOnlyCollection<string> ids, CancellationToken cancellationToken)
    {
        var productIds = ids.Select(id => Guid.TryParse(id, out var guid) ? guid : Guid.Empty)
            .Where(id => id != Guid.Empty)
            .Distinct()
            .ToList();
        if (productIds.Count == 0)
            return [];

        var rows = await Rows().Where(p => productIds.Contains(p.Id)).ToListAsync(cancellationToken);
        return rows.Select(ToDocument).ToList();
    }

    public async IAsyncEnumerable<SearchDocument> StreamAllAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        // Keyset pages by id: short queries, no reader held open while Search writes the engine.
        var after = Guid.Empty;
        while (true)
        {
            var last = after;
            var batch = await Rows().Where(p => p.Id > last).OrderBy(p => p.Id).Take(BatchSize).ToListAsync(cancellationToken);
            foreach (var row in batch)
                yield return ToDocument(row);

            if (batch.Count < BatchSize)
                yield break;
            after = batch[^1].Id;
        }
    }

    public async IAsyncEnumerable<SearchDocumentStamp> StreamStampsAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var after = Guid.Empty;
        while (true)
        {
            var last = after;
            var batch = await dbContext.Products.AsNoTracking().IgnoreBranchScope()
                .Where(p => p.Id > last)
                .OrderBy(p => p.Id)
                .Select(p => new { p.Id, p.Version })
                .Take(BatchSize * 10)
                .ToListAsync(cancellationToken);
            foreach (var row in batch)
                yield return new SearchDocumentStamp(row.Id.ToString(), row.Version);

            if (batch.Count < BatchSize * 10)
                yield break;
            after = batch[^1].Id;
        }
    }

    private IQueryable<ProductSearchRow> Rows() =>
        dbContext.Products.AsNoTracking().IgnoreBranchScope().Select(p => new ProductSearchRow
        {
            Id = p.Id,
            Version = p.Version,
            BranchId = p.BranchId,
            NameEn = p.Name.En,
            NameAr = p.Name.Ar,
            Sku = p.Sku,
            Description = p.Description,
            Status = p.Status,
            Type = p.Type,
            Currency = p.Pricing.Currency,
            Price = p.Pricing.Price,
            CategoryId = p.CategoryId,
            OwnerId = p.OwnerId,
            IsFeatured = p.IsFeatured,
            CreatedAt = p.CreatedAt,
            PublishedAt = p.PublishedAt,
            TagIds = p.Tags.Select(t => t.TagId).ToList(),
            AttributeTypeIds = p.Attributes.Select(a => a.AttributeTypeId).ToList()
        });

    private static SearchDocument ToDocument(ProductSearchRow p) =>
        SearchDocument.For(p.Id, p.Version)
            .InBranch(p.BranchId)
            .Text(Fields.NameEn, p.NameEn)
            .Text(Fields.NameAr, p.NameAr)
            .Text(Fields.Sku, p.Sku)
            .Text(Fields.Description, p.Description)
            .Keyword(Fields.Status, p.Status.ToString())
            .Keyword(Fields.Type, p.Type.ToString())
            .Keyword(Fields.Currency, p.Currency.ToString())
            .Keyword(Fields.CategoryId, p.CategoryId)
            .Keywords(Fields.TagIds, p.TagIds)
            .Keywords(Fields.AttributeTypeIds, p.AttributeTypeIds)
            .Keyword(Fields.OwnerId, p.OwnerId)
            .Bool(Fields.IsFeatured, p.IsFeatured)
            .Number(Fields.Price, p.Price)
            .Date(Fields.CreatedAt, Utc(p.CreatedAt))
            .Date(Fields.PublishedAt, Utc(p.PublishedAt))
            .Build();

    /// <summary>Timestamps are stored as UTC; the provider may still hand them back unspecified.</summary>
    private static DateTime? Utc(DateTime? value) =>
        value is { } v ? v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc) : null;

    private sealed class ProductSearchRow
    {
        public Guid Id { get; init; }
        public long Version { get; init; }
        public Guid? BranchId { get; init; }
        public string NameEn { get; init; } = default!;
        public string NameAr { get; init; } = default!;
        public string Sku { get; init; } = default!;
        public string? Description { get; init; }
        public ProductStatus Status { get; init; }
        public ProductType Type { get; init; }
        public Currency Currency { get; init; }
        public decimal Price { get; init; }
        public Guid? CategoryId { get; init; }
        public Guid OwnerId { get; init; }
        public bool IsFeatured { get; init; }
        public DateTime? CreatedAt { get; init; }
        public DateTime? PublishedAt { get; init; }
        public List<Guid> TagIds { get; init; } = [];
        public List<Guid> AttributeTypeIds { get; init; } = [];
    }
}
