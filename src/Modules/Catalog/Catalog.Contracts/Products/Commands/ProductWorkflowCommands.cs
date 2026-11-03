namespace Catalog.Contracts.Products.Commands;

// Status transitions are critical updates: each carries an optional Version (stale → 409)
// and returns the new Version so clients can chain workflow calls without re-reading.

public record SubmitProductForReviewCommand(Guid ProductId, long? Version = null)
    : ICommand<ProductStatusChangedResult>;

public record ApproveProductCommand(Guid ProductId, string? ReviewNotes = null, long? Version = null)
    : ICommand<ProductStatusChangedResult>;

public record RejectProductCommand(Guid ProductId, string Reason, long? Version = null)
    : ICommand<ProductStatusChangedResult>;

public record RequestProductChangesCommand(Guid ProductId, string Notes, long? Version = null)
    : ICommand<ProductStatusChangedResult>;

public record PublishProductCommand(Guid ProductId, long? Version = null)
    : ICommand<ProductStatusChangedResult>;

public record ArchiveProductCommand(Guid ProductId, long? Version = null)
    : ICommand<ProductStatusChangedResult>;

/// <summary>Archived → Draft (the owner reworks the product and submits it again).</summary>
public record UnarchiveProductCommand(Guid ProductId, long? Version = null)
    : ICommand<ProductStatusChangedResult>;

public record ProductStatusChangedResult(Guid ProductId, ProductStatus Status, long Version);

/// <summary>Admin: feature / unfeature a published product.</summary>
public record ToggleProductFeaturedCommand(Guid ProductId, bool IsFeatured) : ICommand;
