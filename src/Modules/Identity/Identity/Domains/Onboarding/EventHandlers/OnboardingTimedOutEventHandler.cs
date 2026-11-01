using Identity.Contracts.Onboarding.Events;

namespace Identity.Domains.Onboarding.EventHandlers;

/// <summary>
/// Handles compensation logic when an onboarding process times out.
///
/// The state machine already blocks further transitions once the process is TimedOut:
/// VerifyPhoneHandler.GetActiveByUserIdAsync returns null → the user receives
/// "Onboarding has expired" rather than being able to submit a stale OTP.
///
/// OTP codes age out on their own TTL via IVerificationService (no explicit invalidation
/// API is exposed). Any new registration attempt creates a fresh onboarding process.
/// </summary>
public class OnboardingTimedOutEventHandler(
    ILogger<OnboardingTimedOutEventHandler> logger)
    : INotificationHandler<OnboardingTimedOutEvent>
{
    public Task Handle(OnboardingTimedOutEvent notification, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Onboarding timed out for user {UserId}. " +
            "Process is in terminal TimedOut state; further verification attempts are blocked.",
            notification.UserId);

        return Task.CompletedTask;
    }
}
