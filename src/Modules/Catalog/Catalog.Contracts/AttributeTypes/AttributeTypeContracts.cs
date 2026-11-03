namespace Catalog.Contracts.AttributeTypes;

/// <param name="ApplicableTypes">Product types the attribute applies to; null = all types.</param>
public record CreateAttributeTypeCommand(
    LocalizedText Name,
    AttributeDataType DataType,
    string? Unit = null,
    ProductType[]? ApplicableTypes = null)
    : ICommand<CreateAttributeTypeResult>;

public record CreateAttributeTypeResult(Guid Id);

public record UpdateAttributeTypeCommand(
    Guid Id,
    LocalizedText Name,
    AttributeDataType DataType,
    string? Unit = null,
    ProductType[]? ApplicableTypes = null,
    bool IsActive = true,
    long? Version = null)
    : ICommand<UpdateAttributeTypeResult>;

public record UpdateAttributeTypeResult(bool Success, long Version);

/// <summary>Deactivates the attribute type (never a hard delete).</summary>
public record DeleteAttributeTypeCommand(Guid Id) : ICommand<DeleteAttributeTypeResult>;

public record DeleteAttributeTypeResult(bool Success);

/// <param name="ProductType">Only attribute types applicable to this product type (null = all).</param>
public record GetAttributeTypesQuery(bool IncludeInactive = false, ProductType? ProductType = null)
    : IQuery<List<AttributeTypeDto>>;

public record AttributeTypeDto
{
    public Guid Id { get; init; }
    public LocalizedText Name { get; init; } = default!;
    public string DisplayName { get; init; } = string.Empty;
    public AttributeDataType DataType { get; init; }
    public string DataTypeLabel { get; init; } = string.Empty;
    public string? Unit { get; init; }
    public ProductType[]? ApplicableTypes { get; init; }
    public bool IsActive { get; init; }
    public long Version { get; init; }
}
