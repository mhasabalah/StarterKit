using Attachment.Contracts.Attachments.Dtos;
using Attachment.Contracts.Authorization;

namespace Catalog.Domains.Products.Services;

/// <summary>
/// Tells the Attachment module who may attach files to, and read files of, a <see cref="EntityType.Product"/>.
/// Registered in <c>CatalogModule</c> with <c>AddAttachmentEntityPolicy&lt;ProductAttachmentAccessPolicy&gt;(EntityType.Product)</c>.
/// <list type="bullet">
/// <item><b>Attach</b>: the product's owner (while it is not soft-deleted) or an admin.</item>
/// <item><b>Read</b>: anyone for a Published product; otherwise only the owner or an admin, so drafts and
/// rejected listings stay private.</item>
/// </list>
/// The Attachment module always allows admins before calling the policy. The admin check here only keeps the
/// policy correct on its own.
/// </summary>
public sealed class ProductAttachmentAccessPolicy(CatalogDbContext dbContext) : IAttachmentEntityAccessPolicy
{
    public async Task<bool> CanAttachAsync(Guid entityId, AttachmentAccessContext caller,
        CancellationToken cancellationToken = default)
    {
        if (caller.IsAdmin)
        {
            return true;
        }

        if (caller.UserId is not { } userId)
        {
            return false;
        }

        // Global soft-delete filter applies: deleted products can't receive new files.
        return await dbContext.Products.AsNoTracking()
            .AnyAsync(p => p.Id == entityId && p.OwnerId == userId, cancellationToken);
    }

    public async Task<bool> CanReadAsync(Guid entityId, AttachmentAccessContext caller,
        CancellationToken cancellationToken = default)
    {
        if (caller.IsAdmin)
        {
            return true;
        }

        var product = await dbContext.Products.AsNoTracking()
            .Where(p => p.Id == entityId)
            .Select(p => new { p.OwnerId, p.Status })
            .SingleOrDefaultAsync(cancellationToken);

        if (product is null)
        {
            return false;
        }

        return product.Status == ProductStatus.Published || product.OwnerId == caller.UserId;
    }
}
