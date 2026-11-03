namespace Catalog.Contracts.Products.Commands;

/// <summary>Soft delete (sets IsDeleted). Restorable by an admin until the purge job hard-deletes it.</summary>
public record DeleteProductCommand(Guid Id) : ICommand<DeleteProductResult>;

public record DeleteProductResult(bool Success);

/// <summary>Admin: undo a soft delete.</summary>
public record RestoreProductCommand(Guid Id) : ICommand<RestoreProductResult>;

public record RestoreProductResult(bool Success);

/// <summary>
/// Internal (no endpoint): hard-deletes a product that is already soft-deleted. Sent by
/// <c>PurgeSoftDeletedProductsJob</c>; raises ProductPurgedEvent which cleans up attachments.
/// </summary>
public record PurgeProductCommand(Guid Id) : ICommand<PurgeProductResult>;

public record PurgeProductResult(bool Purged);
