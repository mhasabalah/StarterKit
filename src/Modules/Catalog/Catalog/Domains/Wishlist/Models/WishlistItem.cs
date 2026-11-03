using Shared.Core.MultiTenancy;
namespace Catalog.Domains.Wishlist.Models;

/// <summary>
/// A user's saved product. Its own tiny aggregate (IWishlistRepository); unique on {UserId, ProductId}.
/// Deliberately not an IEntity: it gets an explicit query filter that follows the parent product's
/// soft delete (see WishlistItemConfiguration) instead of its own IsDeleted flag.
/// </summary>
public class WishlistItem : IMustHaveTenant
{
    /// <summary>Owning tenant (stamped on insert by TenantEntityInterceptor; immutable).</summary>
    public Guid TenantId { get; set; }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public Guid ProductId { get; private set; }
    public Product Product { get; private set; } = null!;
    public DateTime CreatedAt { get; private set; }

    private WishlistItem()
    {
    }

    public static WishlistItem Create(Guid userId, Guid productId)
    {
        if (userId == Guid.Empty) throw new BadRequestException("User id is required.");
        if (productId == Guid.Empty) throw new BadRequestException("Product id is required.");

        return new WishlistItem
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ProductId = productId,
            CreatedAt = DateTime.UtcNow
        };
    }
}
