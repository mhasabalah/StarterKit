using Attachment.Contracts.Attachments.IntegrationEvents;
using Attachment.Contracts.Attachments.Queries;
using MassTransit;

namespace Identity.Domains.Users.Consumers;

/// <summary>
/// Mirrors the user's current avatar into <c>ApplicationUser.ProfilePictureUrl</c> when the Attachment module reports
/// an avatar change (integration event, at-least-once, runs in the tenant scope the upload happened in).
/// <para>
/// Idempotent and order-independent: the event is only a trigger - the CURRENT avatar is re-read from the Attachment
/// module (<see cref="GetUserAvatarQuery"/>), so duplicates and out-of-order deliveries converge on the newest avatar,
/// and a no-op write is skipped. A missing user is logged and acknowledged (nothing to retry).
/// </para>
/// </summary>
public sealed class UserAvatarChangedConsumer(
    UserManager<ApplicationUser> userManager,
    ISender sender,
    TimeProvider timeProvider,
    ILogger<UserAvatarChangedConsumer> logger) : IConsumer<UserAvatarChangedIntegrationEvent>
{
    public async Task Consume(ConsumeContext<UserAvatarChangedIntegrationEvent> context)
    {
        var message = context.Message;

        var user = await userManager.FindByIdAsync(message.UserId.ToString());
        if (user is null)
        {
            logger.LogWarning("Avatar changed for unknown user {UserId}; ProfilePictureUrl not synced", message.UserId);
            return;
        }

        var current = await sender.Send(new GetUserAvatarQuery(message.UserId), context.CancellationToken);
        if (string.Equals(user.ProfilePictureUrl, current.AvatarUrl, StringComparison.Ordinal))
        {
            return; // duplicate / already up to date
        }

        user.ProfilePictureUrl = current.AvatarUrl;
        user.UpdatedAt = timeProvider.GetUtcNow().UtcDateTime;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            // Throw so the bus retries (e.g. a concurrency stamp conflict with a parallel profile update).
            throw new InvalidOperationException(
                $"Could not sync the avatar of user {message.UserId}: " +
                string.Join("; ", result.Errors.Select(e => e.Code)));
        }

        logger.LogInformation("ProfilePictureUrl synced for user {UserId} (avatar {AttachmentId})",
            message.UserId, current.AttachmentId);
    }
}
