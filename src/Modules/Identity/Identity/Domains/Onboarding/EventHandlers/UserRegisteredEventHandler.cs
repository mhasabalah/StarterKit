using Hangfire;
using Identity.Contracts.Onboarding.Events;
using Identity.Data.Repository.Onboarding;
using Identity.Domains.Onboarding.Jobs;
using Identity.Options;
using Microsoft.Extensions.Options;
using Shared.Notification.Contracts;
using Shared.Notification.Enums;

namespace Identity.Domains.Onboarding.EventHandlers;

public class UserRegisteredEventHandler(
    IOnboardingProcessRepository repository,
    IVerificationService verificationService,
    IBackgroundJobClient backgroundJobClient,
    IOptions<OnboardingOptions> options,
    ILogger<UserRegisteredEventHandler> logger)
    : INotificationHandler<UserRegisteredEvent>
{
    public async Task Handle(UserRegisteredEvent notification, CancellationToken cancellationToken)
    {
        var process = await repository.GetActiveByUserIdAsync(notification.UserId, cancellationToken);

        if (process == null)
        {
            logger.LogWarning("Onboarding process not found for user {UserId}", notification.UserId);
            return;
        }

        var sendResult = await verificationService.SendCodeAsync(
            notification.UserId,
            VerificationPurpose.PhoneVerification,
            NotificationCulture.En,
            cancellationToken);

        if (!sendResult.Success)
        {
            logger.LogCritical(
                "Failed to send onboarding OTP for user {UserId}. Error: {Error}. Scheduling retry in 1 minute.",
                notification.UserId,
                sendResult.Error);
            // Schedule a one-shot retry so transient SMS/WhatsApp failures self-heal.
            // If the retry also fails, the user can call /resend-otp which handles
            // the Registered state explicitly.
            backgroundJobClient.Schedule<OnboardingOtpRetryJob>(
                job => job.ExecuteAsync(notification.UserId, CancellationToken.None),
                TimeSpan.FromMinutes(1));
            return;
        }

        var timeoutMinutes = options.Value.TimeoutMinutes <= 0 ? 60 : options.Value.TimeoutMinutes;
        var jobId = backgroundJobClient.Schedule<OnboardingTimeoutJob>(
            job => job.ExecuteAsync(notification.UserId, CancellationToken.None),
            TimeSpan.FromMinutes(timeoutMinutes));

        process.MarkOtpSent(jobId);
        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Onboarding OTP sent and timeout scheduled for user {UserId}. JobId: {JobId}",
            notification.UserId, jobId);
    }
}
