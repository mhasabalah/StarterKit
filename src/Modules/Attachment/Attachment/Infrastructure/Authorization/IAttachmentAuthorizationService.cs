using Attachment.Data.Repository.Attachments;

namespace Attachment.Infrastructure.Authorization;

public interface IAttachmentAuthorizationService
{
    Task<bool> CanAccessAsync(
        Guid attachmentId,
        Guid userId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default);

    Task<bool> CanModifyAsync(
        Guid attachmentId,
        Guid userId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default);

    Task<bool> CanDeleteAsync(
        Guid attachmentId,
        Guid userId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default);

    Task<bool> CanAccessEntityAsync(
        string entityType,
        Guid entityId,
        Guid userId,
        CancellationToken cancellationToken = default);

    // HTTP Context-aware methods
    Guid? GetCurrentUserId();
    bool IsCurrentUserAdmin();
    Task<bool> CanCurrentUserAccessAsync(Guid attachmentId, CancellationToken cancellationToken = default);
    Task<bool> CanCurrentUserModifyAsync(Guid attachmentId, CancellationToken cancellationToken = default);
    Task<bool> CanCurrentUserDeleteAsync(Guid attachmentId, CancellationToken cancellationToken = default);
}
