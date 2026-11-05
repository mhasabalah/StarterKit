using Shared.Core.Auditing;
using Shared.Core.MultiTenancy;
using Catalog.Contracts.Products.IntegrationEvents;
using Catalog.Domains.Products.Events;

namespace Catalog.Domains.Products.Models;

/// <summary>
/// Product aggregate root. State changes only through the factory and behaviour methods, which enforce
/// invariants and raise id-only domain events.
/// <list type="bullet">
/// <item>Bilingual <see cref="Name"/> (owned <see cref="LocalizedText"/>), owned <see cref="Pricing"/>.</item>
/// <item>Children: image gallery, attribute values (join with payload), tags (pure join).</item>
/// <item>Moderation workflow (<see cref="ProductStatusTransitions"/>) with reviewer fields.</item>
/// <item>True soft delete (<see cref="Delete"/> / <see cref="Restore"/>) — the DbContext filters IsDeleted rows.</item>
/// </list>
/// Every mutation stamps <see cref="UpdatedAt"/>, so the product row is always UPDATEd — and its
/// <c>Version</c> (optimistic concurrency, bumped centrally by AuditableEntityInterceptor) covers the
/// whole aggregate, including child-only changes. Never touch <c>Version</c> here.
/// </summary>
public class Product : Aggregate<Guid>, IMustHaveTenant, IBranchScoped, IAuditable
{
    /// <summary>Owning tenant (stamped on insert by TenantEntityInterceptor; immutable).</summary>
    public Guid TenantId { get; set; }

    /// <summary>
    /// Owning branch; <c>null</c> = tenant-wide (visible to every branch scope). Branch-restricted users only see
    /// products of their branches (named query filter <c>Branch</c>).
    /// </summary>
    public Guid? BranchId { get; set; }

    public const int NameMaxLength = 200;
    public const int DescriptionMaxLength = 2000;
    public const int SkuMaxLength = 64;
    public const int ReviewNotesMaxLength = 1000;
    public const int MaxImages = 10;

    private readonly List<ProductImage> _images = [];
    private readonly List<ProductAttribute> _attributes = [];
    private readonly List<ProductTag> _tags = [];

    public LocalizedText Name { get; private set; } = default!;
    public string? Description { get; private set; }
    public string Sku { get; private set; } = default!;
    public ProductType Type { get; private set; }
    public ProductStatus Status { get; private set; }
    public Pricing Pricing { get; private set; } = default!;

    public Guid? CategoryId { get; private set; }
    public Category? Category { get; private set; }

    /// <summary>The seller (creating user, or the user an admin created it for).</summary>
    public Guid OwnerId { get; private set; }

    /// <summary>Pending-upload session whose files were associated on create.</summary>
    public Guid? AttachmentSessionId { get; private set; }

    public bool IsFeatured { get; private set; }

    /// <summary>Incremented atomically with ExecuteUpdate by the public detail query (no version bump).</summary>
    public int ViewCount { get; private set; }

    // Moderation
    public DateTime? SubmittedForReviewAt { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTime? ReviewedAt { get; private set; }
    public string? ReviewNotes { get; private set; }
    public DateTime? PublishedAt { get; private set; }

    [AuditIgnore] // touched by every mutation (incl. child-only ones): pure noise in the history
    public DateTime? UpdatedAt { get; private set; }
    public DateTime? DeletedAt { get; private set; }

    public IReadOnlyCollection<ProductImage> Images => _images.AsReadOnly();
    public IReadOnlyCollection<ProductAttribute> Attributes => _attributes.AsReadOnly();
    public IReadOnlyCollection<ProductTag> Tags => _tags.AsReadOnly();

    public ProductImage? PrimaryImage => _images.FirstOrDefault(i => i.IsPrimary);
    public bool CanAddImage => _images.Count < MaxImages;

    // EF Core
    private Product()
    {
    }

    public static Product Create(
        LocalizedText name,
        string? description,
        string sku,
        ProductType type,
        Pricing pricing,
        Guid ownerId,
        Guid? categoryId = null,
        Guid? attachmentSessionId = null)
    {
        if (ownerId == Guid.Empty)
            throw new InvalidProductException("Product owner is required.");

        var product = new Product
        {
            Id = Guid.NewGuid(),
            Status = ProductStatus.Draft,
            OwnerId = ownerId,
            AttachmentSessionId = attachmentSessionId
        };
        product.ApplyDetails(name, description, sku, type, pricing, categoryId);

        product.AddDomainEvent(new ProductCreatedEvent(product.Id, product.OwnerId, product.Sku));
        return product;
    }

    public void Update(
        LocalizedText name,
        string? description,
        string sku,
        ProductType type,
        Pricing pricing,
        Guid? categoryId)
    {
        ApplyDetails(name, description, sku, type, pricing, categoryId);
        Touch();
        AddDomainEvent(new ProductUpdatedEvent(Id));
    }

    /// <summary>Canonical SKU form: trimmed, upper-case. Used for uniqueness checks.</summary>
    public static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();

    // ── Attributes (join entity with payload) ───────────────────────────────

    /// <summary>Adds the attribute, or changes its value when the product already has that attribute type.</summary>
    public void AddAttribute(Guid attributeTypeId, string value)
    {
        var existing = _attributes.FirstOrDefault(a => a.AttributeTypeId == attributeTypeId);
        if (existing is null)
        {
            _attributes.Add(ProductAttribute.Create(Id, attributeTypeId, value));
        }
        else
        {
            existing.ChangeValue(value);
        }

        Touch();
    }

    public void RemoveAttribute(Guid attributeTypeId)
    {
        if (_attributes.RemoveAll(a => a.AttributeTypeId == attributeTypeId) > 0)
            Touch();
    }

    public void ClearAttributes()
    {
        if (_attributes.Count == 0) return;
        _attributes.Clear();
        Touch();
    }

    /// <summary>Delta update: removes attribute types not in <paramref name="values"/>, upserts the rest.</summary>
    public void SyncAttributes(IReadOnlyDictionary<Guid, string> values)
    {
        foreach (var removed in _attributes.Select(a => a.AttributeTypeId).Except(values.Keys).ToList())
            RemoveAttribute(removed);

        foreach (var (attributeTypeId, value) in values)
            AddAttribute(attributeTypeId, value);
    }

    // ── Tags (pure join entity) ──────────────────────────────────────────────

    /// <summary>Idempotent.</summary>
    public void AddTag(Guid tagId)
    {
        if (_tags.Any(t => t.TagId == tagId)) return;
        _tags.Add(ProductTag.Create(Id, tagId));
        Touch();
    }

    public void RemoveTag(Guid tagId)
    {
        if (_tags.RemoveAll(t => t.TagId == tagId) > 0)
            Touch();
    }

    public void ClearTags()
    {
        if (_tags.Count == 0) return;
        _tags.Clear();
        Touch();
    }

    /// <summary>Delta update: only removes tags no longer requested and adds new ones (no churn).</summary>
    public void SyncTags(IEnumerable<Guid> tagIds)
    {
        var wanted = tagIds.ToHashSet();
        foreach (var removed in _tags.Select(t => t.TagId).Where(id => !wanted.Contains(id)).ToList())
            RemoveTag(removed);

        foreach (var added in wanted)
            AddTag(added);
    }

    // ── Image gallery ────────────────────────────────────────────────────────
    // Invariants: at most MaxImages; exactly one primary whenever the gallery is non-empty;
    // DisplayOrder is always 0..n-1; an attachment appears at most once.

    public ProductImage AddImage(Guid attachmentId, string url, string? caption = null, bool isPrimary = false)
    {
        if (!CanAddImage)
            throw new InvalidProductException($"A product can have at most {MaxImages} images.");
        if (_images.Any(i => i.AttachmentId == attachmentId))
            throw new InvalidProductException($"Attachment '{attachmentId}' is already in the gallery.");

        var becomesPrimary = isPrimary || _images.Count == 0;
        if (becomesPrimary)
        {
            foreach (var image in _images)
                image.SetPrimary(false);
        }

        var added = ProductImage.Create(Id, attachmentId, url, caption, becomesPrimary, _images.Count);
        _images.Add(added);
        Touch();
        return added;
    }

    /// <summary>Removes the image; promotes the first remaining image when the primary was removed.</summary>
    public ProductImage RemoveImage(Guid imageId)
    {
        var image = FindImage(imageId);
        _images.Remove(image);

        if (image.IsPrimary && _images.Count > 0)
            _images.OrderBy(i => i.DisplayOrder).First().SetPrimary(true);

        Renumber(_images.OrderBy(i => i.DisplayOrder).ToList());
        Touch();
        return image;
    }

    public void SetPrimaryImage(Guid imageId)
    {
        var target = FindImage(imageId);
        foreach (var image in _images)
            image.SetPrimary(image.Id == target.Id);
        Touch();
    }

    /// <param name="orderedImageIds">Must contain every image id of the product exactly once.</param>
    public void ReorderImages(IReadOnlyList<Guid> orderedImageIds)
    {
        if (orderedImageIds.Count != _images.Count ||
            orderedImageIds.Distinct().Count() != orderedImageIds.Count ||
            !orderedImageIds.All(id => _images.Any(i => i.Id == id)))
        {
            throw new InvalidProductException("The new order must list every image of the product exactly once.");
        }

        Renumber(orderedImageIds.Select(FindImage).ToList());
        Touch();
    }

    // ── Moderation workflow ──────────────────────────────────────────────────

    public void SubmitForReview()
    {
        TransitionTo(ProductStatus.PendingReview);
        SubmittedForReviewAt = DateTime.UtcNow;
        AddDomainEvent(new ProductSubmittedForReviewEvent(Id, OwnerId));
    }

    public void Approve(Guid reviewerId, string? notes = null)
    {
        TransitionTo(ProductStatus.Approved);
        RecordReview(reviewerId, notes);
        AddDomainEvent(new ProductApprovedEvent(Id, reviewerId));
    }

    public void Reject(Guid reviewerId, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new InvalidProductException("A rejection reason is required.");

        TransitionTo(ProductStatus.Rejected);
        RecordReview(reviewerId, reason);
        AddDomainEvent(new ProductRejectedEvent(Id, reviewerId, ReviewNotes!));
    }

    public void RequestChanges(Guid reviewerId, string notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            throw new InvalidProductException("Requested changes must be described.");

        TransitionTo(ProductStatus.ChangesRequested);
        RecordReview(reviewerId, notes);
        AddDomainEvent(new ProductChangesRequestedEvent(Id, reviewerId, ReviewNotes!));
    }

    public void Publish()
    {
        TransitionTo(ProductStatus.Published);
        PublishedAt = DateTime.UtcNow;
        AddDomainEvent(new ProductPublishedEvent(Id, OwnerId, Sku, PublishedAt.Value));
        // Cross-module fact: written to the Catalog outbox in the same transaction, delivered at least once.
        AddIntegrationEvent(new ProductPublishedIntegrationEvent
        {
            ProductId = Id,
            OwnerId = OwnerId,
            Sku = Sku,
            PublishedAt = PublishedAt.Value
        });
    }

    public void Archive()
    {
        TransitionTo(ProductStatus.Archived);
        IsFeatured = false;
        AddDomainEvent(new ProductArchivedEvent(Id));
    }

    /// <summary>Archived → Draft, so the owner can rework and resubmit.</summary>
    public void ReturnToDraft()
    {
        TransitionTo(ProductStatus.Draft);
        AddDomainEvent(new ProductReturnedToDraftEvent(Id));
    }

    /// <summary>Only published products can be featured; unfeaturing is always allowed. Idempotent.</summary>
    public void SetFeatured(bool isFeatured)
    {
        if (IsFeatured == isFeatured) return;
        if (isFeatured && Status != ProductStatus.Published)
            throw new InvalidProductException("Only published products can be featured.");

        IsFeatured = isFeatured;
        Touch();
    }

    // ── Soft delete ──────────────────────────────────────────────────────────

    public void Delete()
    {
        if (IsDeleted)
            throw new InvalidProductException("The product is already deleted.");

        IsDeleted = true;
        DeletedAt = DateTime.UtcNow;
        IsFeatured = false;
        Touch();
        AddDomainEvent(new ProductDeletedEvent(Id));
    }

    public void Restore()
    {
        if (!IsDeleted)
            throw new InvalidProductException("The product is not deleted.");

        IsDeleted = false;
        DeletedAt = null;
        Touch();
        AddDomainEvent(new ProductRestoredEvent(Id));
    }

    /// <summary>Raises <see cref="ProductPurgedEvent"/>; the repository performs the hard delete.</summary>
    public void MarkPurged()
    {
        if (!IsDeleted)
            throw new InvalidProductException("Only soft-deleted products can be purged.");

        AddDomainEvent(new ProductPurgedEvent(Id));
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void TransitionTo(ProductStatus target)
    {
        if (!ProductStatusTransitions.CanTransition(Status, target))
            throw new InvalidProductStatusTransitionException(Status, target);

        Status = target;
        Touch();
    }

    private void RecordReview(Guid reviewerId, string? notes)
    {
        if (reviewerId == Guid.Empty)
            throw new InvalidProductException("Reviewer is required.");
        if (notes is not null && notes.Trim().Length > ReviewNotesMaxLength)
            throw new InvalidProductException($"Review notes must not exceed {ReviewNotesMaxLength} characters.");

        ReviewedBy = reviewerId;
        ReviewedAt = DateTime.UtcNow;
        ReviewNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
    }

    private ProductImage FindImage(Guid imageId) =>
        _images.FirstOrDefault(i => i.Id == imageId) ?? throw new ProductImageNotFoundException(Id, imageId);

    private static void Renumber(IReadOnlyList<ProductImage> ordered)
    {
        for (var i = 0; i < ordered.Count; i++)
            ordered[i].SetDisplayOrder(i);
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;

    private void ApplyDetails(
        LocalizedText name,
        string? description,
        string sku,
        ProductType type,
        Pricing pricing,
        Guid? categoryId)
    {
        ArgumentNullException.ThrowIfNull(pricing);

        if (name is null || string.IsNullOrWhiteSpace(name.En) || string.IsNullOrWhiteSpace(name.Ar))
            throw new InvalidProductException("Product name is required in English and Arabic.");
        if (name.En.Trim().Length > NameMaxLength || name.Ar.Trim().Length > NameMaxLength)
            throw new InvalidProductException($"Product name must not exceed {NameMaxLength} characters.");
        if (description is not null && description.Trim().Length > DescriptionMaxLength)
            throw new InvalidProductException(
                $"Product description must not exceed {DescriptionMaxLength} characters.");
        if (string.IsNullOrWhiteSpace(sku))
            throw new InvalidProductException("Product SKU is required.");
        if (sku.Trim().Length > SkuMaxLength)
            throw new InvalidProductException($"Product SKU must not exceed {SkuMaxLength} characters.");
        if (!Enum.IsDefined(type))
            throw new InvalidProductException($"Product type '{type}' is not valid.");
        if (categoryId == Guid.Empty)
            throw new InvalidProductException("Category id must not be empty.");

        // Always a new instance: an owned type instance must never be shared between owners.
        Name = LocalizedText.Create(name.En.Trim(), name.Ar.Trim());
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Sku = NormalizeSku(sku);
        Type = type;
        Pricing = pricing;
        CategoryId = categoryId;
    }
}
