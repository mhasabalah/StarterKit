namespace Identity.Contracts.Onboarding.Commands;

public record RegisterOnboardingCommand(
    string PhoneNumber,
    string FirstName,
    string LastName,
    string Password,
    string ConfirmPassword,
    string? Email
) : ICommand<RegisterOnboardingResult>;

public record RegisterOnboardingResult(
    Guid UserId,
    string Message,
    string Status
);
