using Identity.Contracts.Auth.Commands;
using Identity.Contracts.Passkeys;
using Identity.Domains.Auth.Services;
using Identity.Services.Passkeys;
using Identity.Services.Sso;

namespace Identity.Domains.Passkeys.Features.PasskeyLogin;

/// <summary>Usernameless WebAuthn request options (also for conditional UI / passkey autofill).</summary>
public class BeginPasskeyLoginHandler(IPasskeyService passkeys)
    : ICommandHandler<BeginPasskeyLoginCommand, PasskeyOptionsResult>
{
    public Task<PasskeyOptionsResult> Handle(BeginPasskeyLoginCommand command, CancellationToken cancellationToken)
        => passkeys.BeginAssertionAsync(cancellationToken);
}

public class CompletePasskeyLoginCommandValidator : AbstractValidator<CompletePasskeyLoginCommand>
{
    public CompletePasskeyLoginCommandValidator()
    {
        RuleFor(x => x.CeremonyId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.CredentialJson).NotEmpty().MaximumLength(64 * 1024);
    }
}

/// <summary>
/// Passkey sign-in. User verification (biometric or PIN) is required by <c>Identity:Passkeys</c>, so the passkey is
/// possession + inherence/knowledge: the session is MFA (<c>amr=mfa</c>) and satisfies the privileged-role 2FA rule.
/// The same lockout and deactivation rules as password login apply; failures share one message (no enumeration).
/// </summary>
public class CompletePasskeyLoginHandler(
    IPasskeyService passkeys,
    UserManager<ApplicationUser> userManager,
    ILoginCompletionService loginCompletion,
    ISsoEnforcementPolicy ssoPolicy,
    ILogger<CompletePasskeyLoginHandler> logger) : ICommandHandler<CompletePasskeyLoginCommand, LoginResult>
{
    public async Task<LoginResult> Handle(CompletePasskeyLoginCommand command, CancellationToken cancellationToken)
    {
        var (user, passkey) = await passkeys.CompleteAssertionAsync(
            command.CeremonyId, command.CredentialJson, cancellationToken);

        if (await userManager.IsLockedOutAsync(user))
        {
            logger.LogWarning("Passkey sign-in refused: user {UserId} is locked out", user.Id);
            throw new UnauthorizedException("Account is locked out. Please try again later.");
        }

        if (!user.IsActive)
        {
            logger.LogWarning("Passkey sign-in refused: user {UserId} is deactivated", user.Id);
            throw new UnauthorizedException("Account is disabled.");
        }

        // An organization that enforces SSO owns sign-in for its users (IdP offboarding must lock them out).
        await ssoPolicy.EnsureNonSsoSignInAllowedAsync(user, cancellationToken);

        await userManager.ResetAccessFailedCountAsync(user);

        // Only a user-verified assertion counts as two factors. A presence-only key is one factor, so an account with
        // 2FA still gets its TOTP challenge (only reachable if UserVerification was relaxed in configuration).
        if (!passkey.IsUserVerified && await userManager.GetTwoFactorEnabledAsync(user))
        {
            return await loginCompletion.ChallengeAsync(user, cancellationToken);
        }

        return await loginCompletion.CompleteAsync(user, multiFactor: passkey.IsUserVerified, cancellationToken);
    }
}
