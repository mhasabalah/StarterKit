using Attachment.Domains.Attachments.Events;
using Attachment.Infrastructure.BackgroundJobs;

namespace Attachment.Domains.Attachments.EventHandlers;

/// <summary>
/// A new user avatar (raised when the upload became Ready) replaces the previous ones: they are hidden at once
/// (<c>PendingDeletion</c>, SAVED here) and <c>DeleteAttachmentFilesJob</c> deletes their files and rows.
/// (Identity learns about the new avatar from <c>UserAvatarChangedIntegrationEvent</c>, raised by the aggregate into
/// the outbox in the same transaction as the upload - not from here.)
/// <para>
/// Only reachable for avatars the uploader was allowed to attach (User entity policy: self or admin) or that
/// trusted module code uploaded.
/// </para>
/// </summary>
public class AttachmentUploadedEventHandler(
    IAttachmentRepository repository,
    IAttachmentBackgroundJobs backgroundJobs,
    TimeProvider time,
    ILogger<AttachmentUploadedEventHandler> logger)
    : INotificationHandler<AttachmentUploadedEvent>
{
    public async Task Handle(AttachmentUploadedEvent notification, CancellationToken cancellationToken)
    {
        var attachment = notification.Attachment;

        if (attachment.Category != AttachmentCategory.UserAvatar || !attachment.EntityId.HasValue)
            return;

        var userId = attachment.EntityId.Value;

        var oldAvatars = (await repository.GetByEntityAsync(EntityType.User, userId, cancellationToken))
            .Where(a => a.Category == AttachmentCategory.UserAvatar && a.Id != attachment.Id)
            .ToList();

        if (oldAvatars.Count > 0)
        {
            var now = time.GetUtcNow().UtcDateTime;
            foreach (var old in oldAvatars)
                old.RequestDeletion(now);

            await repository.SaveChangesAsync(cancellationToken);
            backgroundJobs.EnqueueFileDeletion(oldAvatars.Select(a => a.Id).ToList());

            logger.LogInformation("{Count} old avatar(s) of user {UserId} replaced", oldAvatars.Count, userId);
        }
    }
}
