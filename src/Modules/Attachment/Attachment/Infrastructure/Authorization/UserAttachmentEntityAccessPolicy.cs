using Attachment.Contracts.Authorization;

namespace Attachment.Infrastructure.Authorization;

/// <summary>
/// Built-in policy for <see cref="EntityType.User"/>: a user may only attach files to (and read the private
/// files of) their own user entity. Admins are allowed before policies are consulted.
/// </summary>
public sealed class UserAttachmentEntityAccessPolicy : IAttachmentEntityAccessPolicy
{
    public Task<bool> CanAttachAsync(Guid entityId, AttachmentAccessContext caller, CancellationToken cancellationToken = default) =>
        Task.FromResult(IsSelfOrAdmin(entityId, caller));

    public Task<bool> CanReadAsync(Guid entityId, AttachmentAccessContext caller, CancellationToken cancellationToken = default) =>
        Task.FromResult(IsSelfOrAdmin(entityId, caller));

    private static bool IsSelfOrAdmin(Guid entityId, AttachmentAccessContext caller) =>
        caller.IsAdmin || (caller.UserId.HasValue && caller.UserId.Value == entityId);
}
