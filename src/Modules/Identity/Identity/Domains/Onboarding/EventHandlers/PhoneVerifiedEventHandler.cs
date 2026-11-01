using Identity.Contracts.Onboarding.Events;
using Shared.Email.Contracts;
using Shared.Email.Models;
using Shared.Notification.Contracts;
using Shared.Notification.Enums;

namespace Identity.Domains.Onboarding.EventHandlers;

public class PhoneVerifiedEventHandler(
    IOnboardingProcessRepository repository,
    UserManager<ApplicationUser> userManager,
    INotificationSender notificationSender,
    ILogger<PhoneVerifiedEventHandler> logger)
    : INotificationHandler<PhoneVerifiedEvent>
{
    public async Task Handle(PhoneVerifiedEvent notification, CancellationToken cancellationToken)
    {
        var process = await repository.GetByUserIdAndStatusAsync(
            notification.UserId,
            OnboardingStatus.PhoneVerified,
            cancellationToken);
        if (process == null)
        {
            logger.LogWarning(
                "Phone-verified onboarding process not found for user {UserId}",
                notification.UserId);
            return;
        }

        process.MarkComplete();
        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Onboarding completed for user {UserId}", notification.UserId);

        try
        {
            var user = await userManager.FindByIdAsync(notification.UserId.ToString());
            if (user != null && !string.IsNullOrWhiteSpace(user.Email))
            {
                var model = new WelcomeUserModel { FirstName = user.FirstName ?? "User" };

                await notificationSender
                    .For(notification.UserId)
                    .WithEmail(EmailTemplate.WelcomeUser, model.ToDictionary())
                    .SendAsync(forcedChannel: NotificationChannel.Email, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Failed to enqueue welcome notification for user {UserId} — onboarding remains complete",
                notification.UserId);
        }
    }
}
