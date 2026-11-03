using Identity.Contracts.Onboarding.Events;
using Identity.Services.Otp;

namespace Identity.Domains.Onboarding.EventHandlers;

/// <summary>
/// Handles compensation logic when an onboarding process times out.
///
/// The state machine already blocks further transitions once the process is TimedOut:
/// VerifyPhoneHandler.GetActiveByUserIdAsync returns null → the user receives
/// "Onboarding has expired" rather than being able to submit a stale OTP.
///
/// The outstanding onboarding code is invalidated explicitly (IOtpCodeService.InvalidateAsync) so it cannot be
/// used even before its own expiry. Any new registration attempt creates a fresh onboarding process.
/// </summary>
public class OnboardingTimedOutEventHandler(
    IOtpCodeService otpCodeService,
    ILogger<OnboardingTimedOutEventHandler> logger)
    : INotificationHandler<OnboardingTimedOutEvent>
{
    public async Task Handle(OnboardingTimedOutEvent notification, CancellationToken cancellationToken)
    {
        await otpCodeService.InvalidateAsync(
            notification.UserId, OtpPurpose.OnboardingPhoneVerification, cancellationToken);

        logger.LogInformation(
            "Onboarding timed out for user {UserId}. " +
            "Process is in terminal TimedOut state; further verification attempts are blocked.",
            notification.UserId);
    }
}
