namespace Catalog.Domains.Products.Services;

/// <summary>
/// Resource-based authorization for products (who may touch THIS product), complementing the
/// endpoint policies (who may call the route at all).
/// <list type="bullet">
/// <item>Modify (delete, submit, archive, ...): the owner or an admin.</item>
/// <item>Edit content (update, gallery): the owner only while the product is Draft / ChangesRequested /
/// Rejected; admins at any status (moderation fixes).</item>
/// <item>Owner anti-spoofing: only admins may create a product on behalf of someone else.</item>
/// </list>
/// </summary>
public interface IProductAuthorizationService
{
    /// <summary>The authenticated user's id, or null for anonymous calls.</summary>
    Guid? CurrentUserId { get; }

    bool IsAdmin();

    /// <summary>Throws <see cref="ForbiddenException"/> when there is no authenticated user.</summary>
    Guid RequireCurrentUserId();

    /// <summary>Admins may pass an explicit owner; everyone else always owns what they create.</summary>
    Guid ResolveOwnerId(Guid? requestedOwnerId);

    /// <summary>Defence in depth for admin-only commands (the endpoints already require AdminOnly).</summary>
    void EnsureAdmin();

    void EnsureCanModify(Product product, string? message = null);

    void EnsureCanEdit(Product product);
}

public class ProductAuthorizationService(IHttpContextService httpContextService) : IProductAuthorizationService
{
    public Guid? CurrentUserId => httpContextService.GetUserIdAsGuid();

    public bool IsAdmin() =>
        httpContextService.GetRoles().Any(r => r is RoleConstants.Admin or RoleConstants.SuperAdmin);

    public Guid RequireCurrentUserId() =>
        CurrentUserId ?? throw new ForbiddenException("An authenticated user is required.");

    public Guid ResolveOwnerId(Guid? requestedOwnerId)
    {
        var currentUserId = RequireCurrentUserId();
        return IsAdmin() && requestedOwnerId is { } ownerId && ownerId != Guid.Empty ? ownerId : currentUserId;
    }

    public void EnsureAdmin()
    {
        if (!IsAdmin())
            throw new ForbiddenException("Only administrators can perform this action.");
    }

    public void EnsureCanModify(Product product, string? message = null)
    {
        if (IsAdmin() || IsOwner(product))
            return;
        throw new ForbiddenException(message ?? "You do not have permission to modify this product.");
    }

    public void EnsureCanEdit(Product product)
    {
        if (IsAdmin())
            return;

        if (!IsOwner(product))
            throw new ForbiddenException("You do not have permission to modify this product.");

        if (!ProductStatusTransitions.EditableByOwner.Contains(product.Status))
            throw new ForbiddenException($"This product cannot be edited while it is {product.Status}.");
    }

    private bool IsOwner(Product product) => CurrentUserId is { } userId && product.OwnerId == userId;
}
