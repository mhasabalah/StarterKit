using Hangfire;
using Identity.Data.Repository.Onboarding;
using Identity.Options;
using Microsoft.Extensions.Options;
using Identity.Services.Otp;

namespace Identity.Domains.Onboarding.Jobs;

/// <summary>
/// Retries OTP delivery once when the initial send in <see cref="EventHandlers.UserRegisteredEventHandler"/>
/// failed due to a transient channel error. Scheduled 1 minute after the failed initial send.
///
/// On success, advances the process from Registered → OtpSent and schedules the normal timeout job.
/// On failure, logs critical and leaves the process in Registered so the user can call /resend-otp,
/// which handles the Registered state explicitly.
/// </summary>
[Queue("default")]
public class OnboardingOtpRetryJob(
    IOnboardingProcessRepository repository,
    IOtpSender otpSender,
    IBackgroundJobClient backgroundJobClient,
    IOptions<OnboardingOptions> options,
    IDomainEventDispatcher domainEvents,
    ILogger<OnboardingOtpRetryJob> logger)
{
    private readonly OnboardingOptions _options = options.Value;

    public async Task ExecuteAsync(Guid userId, CancellationToken ct = default)
    {
        var process = await repository.GetActiveByUserIdAsync(userId, ct);

        if (process is null || process.Status != OnboardingStatus.Registered)
        {
            // Process already advanced (user called /resend-otp, or registered again).
            logger.LogInformation(
                "OTP retry for user {UserId} skipped — process no longer in Registered state",
                userId);
            return;
        }

        var sendResult = await otpSender.SendAsync(
            userId,
            OtpPurpose.OnboardingPhoneVerification,
            NotificationCulture.En,
            ct);

        if (!sendResult.Success)
        {
            logger.LogCritical(
                "OTP retry also failed for user {UserId}: {Error}. " +
                "User must call /resend-otp to continue registration.",
                userId, sendResult.Error);
            return;
        }

        var timeoutMinutes = _options.TimeoutMinutes <= 0 ? 60 : _options.TimeoutMinutes;
        var jobId = backgroundJobClient.Schedule<OnboardingTimeoutJob>(
            job => job.ExecuteAsync(userId, CancellationToken.None),
            TimeSpan.FromMinutes(timeoutMinutes));

        process.MarkOtpSent(jobId);
        await repository.SaveChangesAsync(ct);

        // Hangfire job = outside MediatR: publish the events of the committed save ourselves.
        await domainEvents.DispatchPendingAsync(ct);

        logger.LogInformation(
            "OTP retry succeeded for user {UserId}. Timeout job scheduled: {JobId}",
            userId, jobId);
    }
}
