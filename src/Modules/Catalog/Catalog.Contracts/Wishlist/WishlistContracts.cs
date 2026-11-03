namespace Catalog.Contracts.Wishlist;

/// <summary>Adds a published product to the current user's wishlist. Duplicate → 409.</summary>
public record AddToWishlistCommand(Guid ProductId) : ICommand<AddToWishlistResult>;

public record AddToWishlistResult(Guid Id);

public record RemoveFromWishlistCommand(Guid ProductId) : ICommand<RemoveFromWishlistResult>;

public record RemoveFromWishlistResult(bool Success);

public record GetWishlistQuery : BaseQuery, IQuery<PaginatedResult<WishlistItemDto>>;

public record WishlistItemDto
{
    public Guid Id { get; init; }
    public Guid ProductId { get; init; }
    public string Name { get; init; } = default!;
    public string Sku { get; init; } = default!;
    public decimal Price { get; init; }
    public decimal? CompareAtPrice { get; init; }
    public Currency Currency { get; init; }
    public string? PrimaryImageUrl { get; init; }

    /// <summary>False when the product was archived (or otherwise unpublished) after being wishlisted.</summary>
    public bool IsAvailable { get; init; }

    public DateTime AddedAt { get; init; }
}
