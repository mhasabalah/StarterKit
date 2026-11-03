using Attachment.Contracts.Attachments.Commands;

namespace Catalog.Domains.Products.Features.DeleteProductImage;

public class DeleteProductImageCommandValidator : AbstractValidator<DeleteProductImageCommand>
{
    public DeleteProductImageCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
        RuleFor(x => x.ImageId).NotEmpty().WithMessage(_ => localizer["Validation.ImageId.Required"]);
    }
}

/// <summary>
/// Removes the image from the gallery first (promoting a new primary if needed), then deletes the file via
/// the Attachment module by its id. Order matters: a failed file delete leaves an orphan file (logged),
/// never a gallery entry pointing at a missing file.
/// </summary>
public class DeleteProductImageHandler(
    IProductRepository repository,
    IProductAuthorizationService authorization,
    ISender sender,
    ILogger<DeleteProductImageHandler> logger)
    : ICommandHandler<DeleteProductImageCommand, DeleteProductImageResult>
{
    public async Task<DeleteProductImageResult> Handle(
        DeleteProductImageCommand command, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(command.ProductId, cancellationToken)
                      ?? throw new ProductNotFoundException(command.ProductId);

        authorization.EnsureCanEdit(product);

        var removed = product.RemoveImage(command.ImageId);
        await repository.SaveChangesAsync(cancellationToken);

        try
        {
            await sender.Send(new DeleteAttachmentCommand(removed.AttachmentId), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Image {ImageId} removed from product {ProductId} but attachment {AttachmentId} " +
                                  "could not be deleted", removed.Id, product.Id, removed.AttachmentId);
        }

        return new DeleteProductImageResult(true);
    }
}
