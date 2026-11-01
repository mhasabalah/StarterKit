using Attachment.Data.Repository.Attachments;

namespace Attachment.Infrastructure.Authorization;

public class AttachmentAuthorizationService(
    IAttachmentRepository attachmentRepository,
    IHttpContextService httpContextService,
    ILogger<AttachmentAuthorizationService> logger) : IAttachmentAuthorizationService
{
    public async Task<bool> CanAccessAsync(
        Guid attachmentId,
        Guid userId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var attachment = await attachmentRepository.GetByIdAsync(attachmentId, cancellationToken);

            if (attachment == null)
            {
                logger.LogWarning("Attachment not found: {AttachmentId}", attachmentId);
                return false;
            }

            // Use domain logic
            return attachment.CanBeAccessedBy(userId, isAdmin);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking access for attachment: {AttachmentId}", attachmentId);
            return false;
        }
    }

    public async Task<bool> CanModifyAsync(
        Guid attachmentId,
        Guid userId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Admins can modify anything
            if (isAdmin)
                return true;

            var attachment = await attachmentRepository.GetByIdAsync(attachmentId, cancellationToken);

            if (attachment == null)
            {
                logger.LogWarning("Attachment not found: {AttachmentId}", attachmentId);
                return false;
            }

            // Only owner can modify
            return attachment.UploadedBy == userId;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking modify permission for attachment: {AttachmentId}", attachmentId);
            return false;
        }
    }

    public async Task<bool> CanDeleteAsync(
        Guid attachmentId,
        Guid userId,
        bool isAdmin = false,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Admins can delete anything
            if (isAdmin)
                return true;

            var attachment = await attachmentRepository.GetByIdAsync(attachmentId, cancellationToken);

            if (attachment == null)
            {
                logger.LogWarning("Attachment not found: {AttachmentId}", attachmentId);
                return false;
            }

            // Only owner can delete
            return attachment.UploadedBy == userId;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking delete permission for attachment: {AttachmentId}", attachmentId);
            return false;
        }
    }

    public async Task<bool> CanAccessEntityAsync(
        string entityType,
        Guid entityId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        // Parse entity type
        if (!Enum.TryParse<EntityType>(entityType, true, out var parsedEntityType))
        {
            logger.LogWarning("Invalid entity type: {EntityType}", entityType);
            return false;
        }

        // Implement entity-level authorization based on business rules
        // Note: This is a basic implementation. For production, integrate with
        // respective module's authorization services
        try
        {
            return parsedEntityType switch
            {
                // For User entities - users can only access their own attachments
                EntityType.User => userId == entityId,

                // For other entities - implement based on business rules
                // TODO: Integrate with the owning module's authorization service, e.g.
                // EntityType.Product => await _productAuthService.CanUserAccessAsync(entityId, userId, cancellationToken),

                // For now, allow access if user is authenticated (basic check)
                // This should be replaced with proper authorization checks
                EntityType.Company => true,
                EntityType.Product => true,
                EntityType.Other => true,

                _ => false // Deny by default for unknown entity types
            };
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error checking entity access: EntityType={EntityType}, EntityId={EntityId}, UserId={UserId}",
                entityType,
                entityId,
                userId);
            return false;
        }
    }

    /// <summary>
    /// Gets current user ID from HTTP context
    /// </summary>
    public Guid? GetCurrentUserId()
    {
        return httpContextService.GetUserIdAsGuid();
    }

    /// <summary>
    /// Checks if current user is admin based on roles
    /// </summary>
    public bool IsCurrentUserAdmin()
    {
        return httpContextService.HasAnyRole("Admin", "SuperAdmin");
    }

    /// <summary>
    /// Checks if current user can access attachment
    /// </summary>
    public async Task<bool> CanCurrentUserAccessAsync(
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            return false;
        }

        var isAdmin = IsCurrentUserAdmin();
        return await CanAccessAsync(attachmentId, userId.Value, isAdmin, cancellationToken);
    }

    /// <summary>
    /// Checks if current user can modify attachment
    /// </summary>
    public async Task<bool> CanCurrentUserModifyAsync(
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            return false;
        }

        var isAdmin = IsCurrentUserAdmin();
        return await CanModifyAsync(attachmentId, userId.Value, isAdmin, cancellationToken);
    }

    /// <summary>
    /// Checks if current user can delete attachment
    /// </summary>
    public async Task<bool> CanCurrentUserDeleteAsync(
        Guid attachmentId,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (!userId.HasValue)
        {
            return false;
        }

        var isAdmin = IsCurrentUserAdmin();
        return await CanDeleteAsync(attachmentId, userId.Value, isAdmin, cancellationToken);
    }
}
