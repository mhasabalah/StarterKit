using Catalog.Data.Configurations;

namespace Catalog.Data.Repository;

public interface IWishlistRepository
{
    Task<WishlistItem?> GetAsync(Guid userId, Guid productId, CancellationToken cancellationToken = default);

    /// <summary>Adds the item; a concurrent duplicate (unique index) becomes a 409 ConflictException.</summary>
    Task AddAsync(WishlistItem item, CancellationToken cancellationToken = default);

    Task RemoveAsync(WishlistItem item, CancellationToken cancellationToken = default);
}

public class WishlistRepository(CatalogDbContext context) : IWishlistRepository
{
    public const string AlreadyWishlistedMessage = "The product is already in your wishlist.";

    public Task<WishlistItem?> GetAsync(Guid userId, Guid productId, CancellationToken cancellationToken = default) =>
        context.WishlistItems.FirstOrDefaultAsync(w => w.UserId == userId && w.ProductId == productId,
            cancellationToken);

    public async Task AddAsync(WishlistItem item, CancellationToken cancellationToken = default)
    {
        await context.WishlistItems.AddAsync(item, cancellationToken);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(WishlistItemConfiguration.UserProductIndexName))
        {
            throw new ConflictException(AlreadyWishlistedMessage);
        }
    }

    public async Task RemoveAsync(WishlistItem item, CancellationToken cancellationToken = default)
    {
        context.WishlistItems.Remove(item);
        await context.SaveChangesAsync(cancellationToken);
    }
}
