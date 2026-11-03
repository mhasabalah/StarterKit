using Identity.Contracts.Audit;

namespace Identity.Contracts.Auth.Commands;

// Self-service two-factor (TOTP, RFC 6238) for the signed-in user. UserId is always the authenticated caller
// (set by the endpoint from the token), never taken from the request body.
// Disable / regenerate / verify check the second factor (with lockout strikes) BEFORE opening their unit of work,
// so they are not IAuditedCommand (a rolled-back transaction would erase the failed-attempt strike).

/// <summary>Starts enrollment: creates a new pending authenticator key (2FA stays off until enabled).</summary>
public record SetupTwoFactorCommand(Guid UserId) : ICommand<SetupTwoFactorResult>;

/// <param name="SharedKey">Base32 key, grouped in blocks of 4 for manual entry.</param>
/// <param name="AuthenticatorUri">otpauth:// URI for a QR code.</param>
public record SetupTwoFactorResult(string SharedKey, string AuthenticatorUri);

/// <summary>Verifies a code from the new authenticator, enables 2FA and returns one-time recovery codes.</summary>
public record EnableTwoFactorCommand(Guid UserId, string Code) : ICommand<EnableTwoFactorResult>, IAuditedCommand;

/// <summary>
/// 2FA is on. Every other session was signed out; <see cref="AccessToken"/>/<see cref="RefreshToken"/> are a new MFA
/// session for this device. <see cref="RecoveryCodes"/> are shown once - only their hashes are stored.
/// </summary>
public record EnableTwoFactorResult(
    IReadOnlyList<string> RecoveryCodes,
    string AccessToken,
    string RefreshToken,
    DateTime ExpiresAt,
    string Message);

/// <summary>Turns 2FA off. Requires the password AND a current authenticator code (or a recovery code).</summary>
public record DisableTwoFactorCommand(Guid UserId, string Password, string Code) : ICommand<DisableTwoFactorResult>;

public record DisableTwoFactorResult(string AccessToken, string RefreshToken, DateTime ExpiresAt, string Message);

/// <summary>Replaces all recovery codes (old ones stop working). Requires a current authenticator code.</summary>
public record RegenerateRecoveryCodesCommand(Guid UserId, string Code) : ICommand<RegenerateRecoveryCodesResult>;

public record RegenerateRecoveryCodesResult(IReadOnlyList<string> RecoveryCodes, string Message);

/// <summary>
/// Second login step: the challenge token from the login response plus EITHER an authenticator code OR a recovery
/// code. Success returns a normal session whose access token carries <c>amr=mfa</c>.
/// </summary>
public record VerifyTwoFactorCommand(string ChallengeToken, string? Code, string? RecoveryCode)
    : ICommand<LoginResult>;
