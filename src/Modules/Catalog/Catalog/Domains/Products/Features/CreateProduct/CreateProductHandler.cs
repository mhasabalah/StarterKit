using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;

namespace Catalog.Domains.Products.Features.CreateProduct;

public class CreateProductCommandValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        Include(new ProductDetailsValidator(localizer));

        RuleFor(x => x.AttachmentSessionId)
            .NotEqual(Guid.Empty).WithMessage(_ => localizer["Validation.Id.Required"])
            .When(x => x.AttachmentSessionId.HasValue);

        RuleFor(x => x.OwnerId)
            .NotEqual(Guid.Empty).WithMessage(_ => localizer["Validation.Id.Required"])
            .When(x => x.OwnerId.HasValue);
    }
}

/// <summary>
/// Creates a Draft product owned by the caller (admins may create on behalf of a seller). Images from a
/// pending-upload session are attached AFTER the product row is committed; failing to attach them is
/// non-fatal (logged, reported as <c>ImagesAssociated = false</c>) so the seller never loses the product.
/// </summary>
public class CreateProductHandler(
    IProductRepository repository,
    IProductAuthorizationService authorization,
    IProductLookupValidator lookupValidator,
    IProductImageValidator imageValidator,
    ISender sender,
    ILogger<CreateProductHandler> logger)
    : ICommandHandler<CreateProductCommand, CreateProductResult>
{
    public async Task<CreateProductResult> Handle(CreateProductCommand command, CancellationToken cancellationToken)
    {
        var ownerId = authorization.ResolveOwnerId(command.OwnerId);

        var sku = Product.NormalizeSku(command.Sku);
        if (await repository.SkuExistsAsync(sku, cancellationToken: cancellationToken))
            throw new ProductSkuAlreadyExistsException(sku);

        var tagIds = command.TagIds ?? [];
        var attributes = command.Attributes ?? [];
        await lookupValidator.EnsureValidAsync(command.Type, command.CategoryId, tagIds, attributes, cancellationToken);

        var product = Product.Create(
            command.Name,
            command.Description,
            command.Sku,
            command.Type,
            Pricing.Create(command.Price, command.CompareAtPrice, command.Currency),
            ownerId,
            command.CategoryId,
            command.AttachmentSessionId);

        foreach (var tagId in tagIds)
            product.AddTag(tagId);

        foreach (var attribute in attributes)
            product.AddAttribute(attribute.AttributeTypeId, attribute.Value);

        await repository.AddAsync(product, cancellationToken);

        var imagesAssociated = await TryAssociateImagesAsync(product, cancellationToken);

        return new CreateProductResult(product.Id, imagesAssociated);
    }

    private async Task<bool> TryAssociateImagesAsync(Product product, CancellationToken cancellationToken)
    {
        if (product.AttachmentSessionId is not { } sessionId)
            return true;

        try
        {
            var associated = await sender.Send(
                new AssociatePendingAttachmentsCommand(sessionId, EntityType.Product, product.Id,
                    authorization.RequireCurrentUserId(), IsPublic: true),
                cancellationToken);

            if (associated.TotalAssociated == 0)
            {
                logger.LogWarning("No pending attachments found for session {SessionId} (product {ProductId})",
                    sessionId, product.Id);
                return false;
            }

            foreach (var file in associated.Metadata.OrderBy(m => m.DisplayOrder))
            {
                if (!product.CanAddImage) break;

                if (!await imageValidator.IsImageAsync(file.Id, cancellationToken))
                {
                    logger.LogWarning("Attachment {AttachmentId} of session {SessionId} is not an image; skipped",
                        file.Id, sessionId);
                    continue;
                }

                product.AddImage(file.Id, file.FileUrl, isPrimary: file.IsPrimary);
            }

            await repository.SaveChangesAsync(cancellationToken);
            return product.Images.Count > 0;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex,
                "Product {ProductId} was created but images from session {SessionId} could not be attached",
                product.Id, sessionId);
            return false;
        }
    }
}
