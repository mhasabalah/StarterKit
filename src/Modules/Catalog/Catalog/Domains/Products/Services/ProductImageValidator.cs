using Attachment.Contracts.Attachments.Dtos;
using Attachment.Contracts.Attachments.Queries;

namespace Catalog.Domains.Products.Services;

/// <summary>
/// Cross-module boundary example: Catalog never references the Attachment implementation or its
/// DbContext — it only sends a query from <c>Attachment.Contracts</c> via ISender.
/// Used when a pending-upload session is attached to a new product: only real images join the gallery.
/// </summary>
public interface IProductImageValidator
{
    /// <summary>Throws <see cref="InvalidProductException"/> when the attachment is missing, inaccessible or not an image.</summary>
    Task EnsureValidImageAsync(Guid attachmentId, CancellationToken cancellationToken = default);

    /// <summary>Non-throwing variant: false when the attachment cannot be used as a product image.</summary>
    Task<bool> IsImageAsync(Guid attachmentId, CancellationToken cancellationToken = default);
}

public class ProductImageValidator(ISender sender) : IProductImageValidator
{
    public async Task EnsureValidImageAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        GetAttachmentByIdResult result;
        try
        {
            result = await sender.Send(new GetAttachmentByIdQuery(attachmentId), cancellationToken);
        }
        catch (NotFoundException)
        {
            throw new InvalidProductException($"Image attachment '{attachmentId}' was not found.");
        }
        catch (UnauthorizedAccessException)
        {
            throw new InvalidProductException(
                $"Image attachment '{attachmentId}' is not accessible to the current user.");
        }

        if (result.Attachment.Type != AttachmentType.Image)
            throw new InvalidProductException($"Attachment '{attachmentId}' is not an image.");
    }

    public async Task<bool> IsImageAsync(Guid attachmentId, CancellationToken cancellationToken = default)
    {
        try
        {
            await EnsureValidImageAsync(attachmentId, cancellationToken);
            return true;
        }
        catch (InvalidProductException)
        {
            return false;
        }
    }
}
