using Attachment.Contracts.Events;

namespace Identity.Domains.Users.EventHandlers;

public class UserAvatarUploadedEventHandler(
    UserManager<ApplicationUser> userManager,
    ILogger<UserAvatarUploadedEventHandler> logger)
    : INotificationHandler<UserAvatarUploadedEvent>
{
    public async Task Handle(UserAvatarUploadedEvent notification, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(notification.UserId.ToString());
        if (user is null)
        {
            logger.LogWarning(
                "UserAvatarUploadedEvent: user {UserId} not found — ProfilePictureUrl not synced",
                notification.UserId);
            return;
        }

        user.ProfilePictureUrl = notification.AvatarUrl;
        user.UpdatedAt = DateTime.UtcNow;

        await userManager.UpdateAsync(user);

        logger.LogInformation(
            "ProfilePictureUrl synced for user {UserId}", notification.UserId);
    }
}
