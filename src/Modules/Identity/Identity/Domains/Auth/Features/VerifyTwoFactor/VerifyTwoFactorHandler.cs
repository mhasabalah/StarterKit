using Identity.Contracts.Auth.Commands;
using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Domains.Auth.Services;
using Identity.Services.Token;
using Identity.Services.TwoFactor;

namespace Identity.Domains.Auth.Features.VerifyTwoFactor;

public class VerifyTwoFactorCommandValidator : AbstractValidator<VerifyTwoFactorCommand>
{
    public VerifyTwoFactorCommandValidator()
    {
        RuleFor(x => x.ChallengeToken).NotEmpty().WithMessage("The two-factor challenge token is required");

        RuleFor(x => x)
            .Must(x => string.IsNullOrWhiteSpace(x.Code) != string.IsNullOrWhiteSpace(x.RecoveryCode))
            .WithName("Code")
            .WithMessage("Send either an authenticator code or a recovery code");
    }
}

/// <summary>
/// Second login step. Validates the challenge token (signature, expiry, user, security stamp), then the authenticator
/// or recovery code. Every miss is a lockout strike (Identity AccessFailed), persisted immediately; the endpoint is
/// also rate-limited ("auth"). Success issues a normal session flagged as MFA (<c>amr=mfa</c>).
/// </summary>
public class VerifyTwoFactorHandler(
    UserManager<ApplicationUser> userManager,
    ITwoFactorChallengeService challengeService,
    ITwoFactorService twoFactor,
    ILoginCompletionService loginCompletion,
    IIdentityAuditWriter audit,
    IIdentityUnitOfWork unitOfWork,
    ILogger<VerifyTwoFactorHandler> logger)
    : ICommandHandler<VerifyTwoFactorCommand, LoginResult>
{
    public const string InvalidChallengeMessage = "The two-factor challenge is invalid or expired. Sign in again.";
    public const string InvalidCodeMessage = "Invalid two-factor code.";

    public async Task<LoginResult> Handle(VerifyTwoFactorCommand command, CancellationToken cancellationToken)
    {
        var challenge = challengeService.Validate(command.ChallengeToken)
                        ?? throw new UnauthorizedException(InvalidChallengeMessage);

        var user = await userManager.FindByIdAsync(challenge.UserId.ToString());
        if (user is null ||
            !string.Equals(TokenHasher.HashSecurityStamp(user.SecurityStamp), challenge.SecurityStampHash,
                StringComparison.Ordinal))
        {
            logger.LogWarning("Two-factor verify rejected: unknown user or stale challenge ({UserId})", challenge.UserId);
            throw new UnauthorizedException(InvalidChallengeMessage);
        }

        if (!user.IsActive)
        {
            throw new UnauthorizedException("Account is disabled.");
        }

        if (!await userManager.GetTwoFactorEnabledAsync(user))
        {
            throw new UnauthorizedException(InvalidChallengeMessage);
        }

        // Throws when locked out; records the strike (outside any transaction) on a miss.
        var factor = await twoFactor.VerifySecondFactorAsync(
            user, command.Code, command.RecoveryCode, allowRecoveryCode: true);
        if (factor == SecondFactorResult.Failed)
        {
            throw new UnauthorizedException(InvalidCodeMessage);
        }

        return await unitOfWork.ExecuteAsync(async () =>
        {
            if (factor == SecondFactorResult.RecoveryCode)
            {
                var remaining = await userManager.CountRecoveryCodesAsync(user);
                audit.Record(IdentityAuditActions.TwoFactorRecoveryCodeUsed, user.Id,
                    after: new { RemainingRecoveryCodes = remaining }, actorId: user.Id);
                logger.LogWarning("User {UserId} signed in with a recovery code ({Remaining} left)", user.Id, remaining);
            }

            return await loginCompletion.CompleteAsync(user, multiFactor: true, cancellationToken);
        }, cancellationToken);
    }
}
