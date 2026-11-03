using Attachment.Contracts.Attachments.Commands;
using Attachment.Contracts.Attachments.Dtos;

namespace Catalog.Domains.Products.Features.UploadProductImage;

public class UploadProductImageCommandValidator : AbstractValidator<UploadProductImageCommand>
{
    /// <summary>Catalog-level cap; the Attachment module applies its own configured limits too.</summary>
    public const long MaxFileSizeBytes = 10 * 1024 * 1024;

    public UploadProductImageCommandValidator(IStringLocalizer<CatalogResource> localizer)
    {
        RuleFor(x => x.ProductId).NotEmpty().WithMessage(_ => localizer["Validation.ProductId.Required"]);
        RuleFor(x => x.FileName).NotEmpty().WithMessage(_ => localizer["Validation.File.Required"]);
        RuleFor(x => x.FileStream).NotNull().WithMessage(_ => localizer["Validation.File.Required"]);
        RuleFor(x => x.FileSize)
            .GreaterThan(0).WithMessage(_ => localizer["Validation.File.Required"])
            .LessThanOrEqualTo(MaxFileSizeBytes).WithMessage(_ => localizer["Validation.File.TooLarge"]);
        RuleFor(x => x.ContentType)
            .Must(ct => ct is not null && ct.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .WithMessage(_ => localizer["Validation.ContentType.Image"]);
        RuleFor(x => x.Caption)
            .MaximumLength(ProductImage.CaptionMaxLength).WithMessage(_ => localizer["Validation.Caption.MaxLength"])
            .When(x => x.Caption is not null);
    }
}

/// <summary>
/// Multipart upload straight into the gallery: the file goes to the Attachment module (cross-module
/// command via ISender, linked to the product), then the aggregate records it. If saving the product fails,
/// the uploaded attachment is deleted again (compensation) so no orphan file is left behind.
/// </summary>
public class UploadProductImageHandler(
    IProductRepository repository,
    IProductAuthorizationService authorization,
    ISender sender,
    ILogger<UploadProductImageHandler> logger)
    : ICommandHandler<UploadProductImageCommand, UploadProductImageResult>
{
    public async Task<UploadProductImageResult> Handle(
        UploadProductImageCommand command, CancellationToken cancellationToken)
    {
        var product = await repository.GetByIdAsync(command.ProductId, cancellationToken)
                      ?? throw new ProductNotFoundException(command.ProductId);

        authorization.EnsureCanEdit(product);

        // Check before uploading so a full gallery does not cost a wasted upload.
        if (!product.CanAddImage)
            throw new InvalidProductException($"A product can have at most {Product.MaxImages} images.");

        var upload = await sender.Send(new UploadAttachmentCommand(
            command.FileName,
            command.ContentType,
            command.FileSize,
            command.FileStream,
            AttachmentType.Image,
            EntityType.Product,
            product.Id,
            command.Caption,
            IsPublic: true,
            GenerateThumbnail: true), cancellationToken);

        try
        {
            var image = product.AddImage(upload.Id, upload.FileUrl, command.Caption, command.IsPrimary);
            await repository.SaveChangesAsync(cancellationToken);
            return new UploadProductImageResult(image.Id, upload.Id, image.Url, image.IsPrimary);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Saving image for product {ProductId} failed; deleting attachment {AttachmentId}",
                product.Id, upload.Id);
            await sender.Send(new DeleteAttachmentCommand(upload.Id), CancellationToken.None);
            throw;
        }
    }
}
