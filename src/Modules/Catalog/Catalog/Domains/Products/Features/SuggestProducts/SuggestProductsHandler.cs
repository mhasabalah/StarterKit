using Catalog.Domains.Products.SearchIndex;
using Catalog.Options;
using Microsoft.Extensions.Options;
using Search.Contracts.Querying;

namespace Catalog.Domains.Products.Features.SuggestProducts;

public class SuggestProductsQueryValidator : AbstractValidator<SuggestProductsQuery>
{
    public SuggestProductsQueryValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.Q)
            .NotEmpty().WithMessage(_ => localizer["Validation.SearchTerm.Required"])
            .MaximumLength(CatalogValidationRules.SearchTermMaxLength)
            .WithMessage(_ => localizer["Validation.SearchTerm.MaxLength"]);

        RuleFor(x => x.Limit)
            .InclusiveBetween(1, SuggestProductsQuery.MaxLimit)
            .When(x => x.Limit.HasValue)
            .WithMessage(_ => localizer["Validation.SuggestLimit.Range", SuggestProductsQuery.MaxLimit]);
    }
}

/// <summary>
/// As-you-type suggestions from the search index (autocomplete mode: the last word is a prefix; close spellings when
/// nothing matches), published products only, re-checked through <see cref="CatalogDbContext"/>. While the index is
/// off or cannot answer, the list is empty: suggestions are a convenience, the search endpoint is the fallback.
/// </summary>
public class SuggestProductsHandler(
    CatalogDbContext dbContext,
    ISender sender,
    IOptions<CatalogOptions> options)
    : IQueryHandler<SuggestProductsQuery, List<ProductSuggestionDto>>
{
    public async Task<List<ProductSuggestionDto>> Handle(SuggestProductsQuery query, CancellationToken cancellationToken)
    {
        if (!options.Value.Search.UseSearchIndex)
            return [];

        var result = await sender.Send(new SearchDocumentsQuery
        {
            IndexKey = ProductSearchIndex.Key,
            Text = query.Q,
            Mode = SearchMode.Autocomplete,
            Filters = [new KeywordFilter(ProductSearchIndex.Fields.Status, nameof(ProductStatus.Published))],
            PageSize = query.Limit ?? SuggestProductsQuery.DefaultLimit,
            Highlight = true
        }, cancellationToken);
        if (result.Status != SearchStatus.Ok || result.Hits.Count == 0)
            return [];

        var ids = result.Hits.Select(h => Guid.TryParse(h.Id, out var id) ? id : Guid.Empty).Where(id => id != Guid.Empty).ToList();
        var products = await dbContext.Products.AsNoTracking()
            .Where(p => ids.Contains(p.Id) && p.Status == ProductStatus.Published)
            .Select(p => new { p.Id, NameEn = p.Name.En, NameAr = p.Name.Ar, p.Sku })
            .ToDictionaryAsync(p => p.Id, cancellationToken);

        var arabic = CatalogCulture.IsArabic();
        var (nameField, otherField) = arabic
            ? (ProductSearchIndex.Fields.NameAr, ProductSearchIndex.Fields.NameEn)
            : (ProductSearchIndex.Fields.NameEn, ProductSearchIndex.Fields.NameAr);

        return result.Hits
            .Where(hit => Guid.TryParse(hit.Id, out var id) && products.ContainsKey(id))
            .Select(hit =>
            {
                var product = products[Guid.Parse(hit.Id)];
                var highlight = hit.Highlights.GetValueOrDefault(nameField) ?? hit.Highlights.GetValueOrDefault(otherField);
                return new ProductSuggestionDto(product.Id, arabic ? product.NameAr : product.NameEn, product.Sku, highlight);
            })
            .ToList();
    }
}
