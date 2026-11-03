using Identity.Contracts.Auth.Dtos;

namespace Identity.Contracts.Auth.Commands;

public record LoginCommand(
    string Identifier,  // Email OR Phone
    string Password
) : ICommand<LoginResult>;

/// <summary>
/// Result of a login step. Three shapes:
/// <list type="bullet">
/// <item>Signed in: <see cref="AccessToken"/> / <see cref="RefreshToken"/> set.</item>
/// <item>Second factor needed: <see cref="RequiresTwoFactor"/> = true, no tokens, and a short-lived
/// <see cref="TwoFactorChallengeToken"/> to send to <c>POST /api/auth/2fa/verify</c> with the authenticator code.</item>
/// <item>Partial login (login policy): no tokens, <see cref="Message"/> says why.</item>
/// </list>
/// </summary>
public record LoginResult(
    string? AccessToken,
    string? RefreshToken,
    DateTime? ExpiresAt,
    UserDto User,
    string Message
)
{
    public bool RequiresTwoFactor { get; init; }

    /// <summary>NOT an access token: only accepted by <c>POST /api/auth/2fa/verify</c>.</summary>
    public string? TwoFactorChallengeToken { get; init; }

    public DateTime? TwoFactorChallengeExpiresAt { get; init; }
}
