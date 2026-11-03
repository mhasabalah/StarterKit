namespace Catalog.Contracts.Products.Commands;

/// <summary>
/// The editable product fields shared by create and update, so both commands are validated by one
/// rule set (DRY) — see ProductDetailsValidator in the Catalog module.
/// </summary>
public interface IProductDetails
{
    LocalizedText Name { get; }
    string? Description { get; }
    string Sku { get; }
    ProductType Type { get; }
    decimal Price { get; }
    decimal? CompareAtPrice { get; }
    Currency Currency { get; }
    Guid? CategoryId { get; }
    List<Guid>? TagIds { get; }
    List<ProductAttributeInput>? Attributes { get; }
}

/// <summary>An attribute value for a product (e.g. Color = "Red"). Validated against the attribute type's data type.</summary>
public record ProductAttributeInput(Guid AttributeTypeId, string Value);

/// <param name="OwnerId">Honoured only for admins (create on behalf of a seller). Non-admins always own what they create.</param>
/// <param name="AttachmentSessionId">Pending-upload session (Attachment module) whose images become the gallery.</param>
public record CreateProductCommand(
    LocalizedText Name,
    string? Description,
    string Sku,
    ProductType Type,
    decimal Price,
    decimal? CompareAtPrice,
    Currency Currency,
    Guid? CategoryId = null,
    List<Guid>? TagIds = null,
    List<ProductAttributeInput>? Attributes = null,
    Guid? AttachmentSessionId = null,
    Guid? OwnerId = null)
    : ICommand<CreateProductResult>, IProductDetails;

/// <param name="ImagesAssociated">False when a session was supplied but its images could not be attached (non-fatal).</param>
public record CreateProductResult(Guid Id, bool ImagesAssociated);
