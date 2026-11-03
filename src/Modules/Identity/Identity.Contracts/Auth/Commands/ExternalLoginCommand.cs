namespace Identity.Contracts.Auth.Commands;

public record ExternalLoginCommand(
    string Provider,
    string ReturnUrl
) : ICommand<ExternalLoginResult>;

public record ExternalLoginResult(
    string RedirectUrl
);

public record ExternalLoginCallbackCommand(
    string? RemoteError
) : ICommand<ExternalLoginCallbackResult>;

public record ExternalLoginCallbackResult(
    Guid UserId,
    bool IsNewUser,
    bool RequiresPhoneVerification,
    string? AccessToken,
    string? RefreshToken,
    DateTime? ExpiresAt,
    string Message
)
{
    /// <summary>The account has 2FA enabled: no tokens; finish with POST /api/auth/2fa/verify.</summary>
    public bool RequiresTwoFactor { get; init; }

    public string? TwoFactorChallengeToken { get; init; }

    public DateTime? TwoFactorChallengeExpiresAt { get; init; }
}
