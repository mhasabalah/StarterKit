namespace Catalog.Contracts.Products.Dtos;

/// <summary>
/// Read model for a product. <see cref="Version"/> must be round-tripped by clients
/// on update to get optimistic-concurrency protection (stale version → HTTP 409).
/// </summary>
public record ProductDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string? Description { get; init; }
    public string Sku { get; init; } = default!;
    public decimal Price { get; init; }
    public bool IsActive { get; init; }
    public Guid? ImageAttachmentId { get; init; }
    public DateTime? CreatedAt { get; init; }
    public DateTime? LastModified { get; init; }
    public long Version { get; init; }
}
