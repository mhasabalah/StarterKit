namespace Catalog.Domains.Products.Models;

/// <summary>
/// The workflow as an allow-list: anything not listed here is rejected. Keeping it as data (instead of
/// scattered if-statements) makes the state machine reviewable at a glance and testable exhaustively.
/// </summary>
public static class ProductStatusTransitions
{
    private static readonly IReadOnlyDictionary<ProductStatus, ProductStatus[]> Allowed =
        new Dictionary<ProductStatus, ProductStatus[]>
        {
            [ProductStatus.Draft] = [ProductStatus.PendingReview],
            [ProductStatus.ChangesRequested] = [ProductStatus.PendingReview],
            [ProductStatus.Rejected] = [ProductStatus.PendingReview],
            [ProductStatus.PendingReview] =
                [ProductStatus.Approved, ProductStatus.Rejected, ProductStatus.ChangesRequested],
            [ProductStatus.Approved] = [ProductStatus.Published],
            [ProductStatus.Published] = [ProductStatus.Archived],
            [ProductStatus.Archived] = [ProductStatus.Draft],
        };

    /// <summary>Statuses in which the owner may edit content (name, price, gallery, ...).</summary>
    public static readonly IReadOnlySet<ProductStatus> EditableByOwner = new HashSet<ProductStatus>
    {
        ProductStatus.Draft,
        ProductStatus.ChangesRequested,
        ProductStatus.Rejected,
    };

    public static bool CanTransition(ProductStatus from, ProductStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    public static IReadOnlyCollection<ProductStatus> AllowedFrom(ProductStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : [];
}
