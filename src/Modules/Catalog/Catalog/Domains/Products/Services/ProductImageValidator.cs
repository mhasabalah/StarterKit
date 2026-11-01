using Attachment.Contracts.Attachments.Dtos;
using Attachment.Contracts.Attachments.Queries;

namespace Catalog.Domains.Products.Services;

/// <summary>
/// Cross-module boundary example: Catalog never references the Attachment implementation
/// or its DbContext — it only sends a query from <c>Attachment.Contracts</c> via ISender.
/// </summary>
public interface IProductImageValidator
{
    /// <summary>Throws <see cref="InvalidProductException"/> when the attachment is unusable.</summary>
    Task EnsureValidImageAsync(Guid attachmentId, CancellationToken cancellationToken = default);
}

public class ProductImageValidator(ISender sender) : IProductImageValidator
{
    public async Task EnsureValidImageAsync(
        Guid attachmentId, CancellationToken cancellationToken = default)
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
        {
            throw new InvalidProductException(
                $"Attachment '{attachmentId}' is not an image.");
        }
    }
}
