namespace Identity.Contracts.Onboarding.Commands;

public record ResendOnboardingOtpCommand(
    Guid UserId
) : ICommand<ResendOnboardingOtpResult>;

public record ResendOnboardingOtpResult(
    string Message,
    int RemainingResends
);
