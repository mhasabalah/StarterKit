using Attachment.Contracts.Events;
using Attachment.Domains.Attachments.Events;
using Attachment.Infrastructure.Storage;

namespace Attachment.Domains.Attachments.EventHandlers;

public class AttachmentUploadedEventHandler(
    IAttachmentRepository repository,
    IStorageService storageService,
    IPublisher publisher,
    ILogger<AttachmentUploadedEventHandler> logger)
    : INotificationHandler<AttachmentUploadedEvent>
{
    public async Task Handle(AttachmentUploadedEvent notification, CancellationToken cancellationToken)
    {
        var attachment = notification.Attachment;

        if (attachment.Category != AttachmentCategory.UserAvatar || !attachment.EntityId.HasValue)
            return;

        var userId = attachment.EntityId.Value;

        // Delete all previous avatars for this user now that the new one is saved
        var existing = await repository.GetByEntityAsync(EntityType.User, userId, cancellationToken);
        var oldAvatars = existing
            .Where(a => a.Category == AttachmentCategory.UserAvatar && a.Id != attachment.Id)
            .ToList();

        if (oldAvatars.Count > 0)
        {
            var storagePaths = oldAvatars
                .SelectMany(a => new[] { a.StoragePath, a.ThumbnailPath })
                .Where(p => p is not null)
                .Select(p => p!)
                .ToList();

            try
            {
                await storageService.DeleteBatchAsync(storagePaths, cancellationToken);
            }
            catch (Exception ex)
            {
                // Log but don't fail the upload — orphaned files can be cleaned by a background job
                logger.LogWarning(ex,
                    "Failed to delete old avatar storage files for user {UserId}. Paths: {Paths}",
                    userId, string.Join(", ", storagePaths));
            }

            foreach (var old in oldAvatars)
                await repository.DeleteAsync(old.Id, cancellationToken);

            logger.LogInformation(
                "Deleted {Count} old avatar(s) for user {UserId}", oldAvatars.Count, userId);
        }

        // Notify Identity to sync ApplicationUser.ProfilePictureUrl
        await publisher.Publish(new UserAvatarUploadedEvent(userId, attachment.FileUrl), cancellationToken);
    }
}
