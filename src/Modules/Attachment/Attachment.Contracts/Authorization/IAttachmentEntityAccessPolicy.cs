using Attachment.Contracts.Attachments.Dtos;

namespace Attachment.Contracts.Authorization;

/// <summary>
/// Decides who may attach files to, and read the private files of, one kind of entity
/// (<see cref="EntityType"/>).
/// <para>
/// The Attachment module does not know the business rules of other modules, so the module that owns
/// an entity registers a policy for it:
/// <code>
/// // In CatalogModule.AddCatalogModule(...)
/// services.AddAttachmentEntityPolicy&lt;ProductAttachmentAccessPolicy&gt;(EntityType.Product);
/// </code>
/// </para>
/// <para>
/// <b>Deny by default:</b> when no policy is registered for an entity type, the Attachment HTTP endpoints
/// refuse to attach files to that entity and only list its <c>IsPublic</c> files. Admins
/// (<c>Admin</c>/<c>SuperAdmin</c>) are always allowed and policies are not asked for them.
/// </para>
/// <para>
/// Policies are resolved per request (scoped). They may use <c>ISender</c> to query their own module.
/// </para>
/// </summary>
public interface IAttachmentEntityAccessPolicy
{
    /// <summary>True when <paramref name="caller"/> may upload/associate/delete files of the entity.</summary>
    Task<bool> CanAttachAsync(Guid entityId, AttachmentAccessContext caller, CancellationToken cancellationToken = default);

    /// <summary>True when <paramref name="caller"/> may read the private (non-public) files of the entity.</summary>
    Task<bool> CanReadAsync(Guid entityId, AttachmentAccessContext caller, CancellationToken cancellationToken = default);
}

/// <summary>The caller an access decision is made for.</summary>
/// <param name="UserId">Authenticated user id, or null for an anonymous caller.</param>
/// <param name="IsAdmin">True for the Admin or SuperAdmin role.</param>
/// <param name="Roles">All role names of the caller (case-insensitive set).</param>
public sealed record AttachmentAccessContext(Guid? UserId, bool IsAdmin, IReadOnlySet<string> Roles)
{
    public bool IsAuthenticated => UserId.HasValue;

    public static AttachmentAccessContext Anonymous { get; } =
        new(null, false, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
}
