using Identity.Contracts.Auth.Dtos;

namespace Identity.Contracts.Onboarding.Commands;

public record VerifyOnboardingPhoneCommand(
    Guid UserId,
    string Code
) : ICommand<VerifyOnboardingPhoneResult>;

public record VerifyOnboardingPhoneResult(
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    UserDto User
);
