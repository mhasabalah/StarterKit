namespace Catalog.Contracts.Products.Queries;

/// <summary>
/// As-you-type suggestions over published products: the last word also matches as a prefix ("lap" finds "laptop"), on
/// the name (en/ar) and the SKU. Answered from the search index; an empty list while the index is not available.
/// </summary>
public record SuggestProductsQuery : IQuery<List<ProductSuggestionDto>>
{
    public const int MaxLimit = 20;
    public const int DefaultLimit = 8;

    public string Q { get; init; } = string.Empty;

    /// <summary>1 to <see cref="MaxLimit"/>; <see cref="DefaultLimit"/> when omitted.</summary>
    public int? Limit { get; init; }
}

/// <param name="Name">The name in the request culture.</param>
/// <param name="Highlight">The name with the typed text in <c>&lt;mark&gt;</c> (HTML-encoded otherwise), when it matched.</param>
public sealed record ProductSuggestionDto(Guid Id, string Name, string Sku, string? Highlight);
