using Identity.Contracts.Auth.Commands;
using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Services.TwoFactor;

namespace Identity.Domains.Auth.Features.RegenerateRecoveryCodes;

public class RegenerateRecoveryCodesCommandValidator : AbstractValidator<RegenerateRecoveryCodesCommand>
{
    public RegenerateRecoveryCodesCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Code).NotEmpty().WithMessage("The authenticator code is required");
    }
}

/// <summary>Replaces every recovery code. Requires a current authenticator code (counts towards lockout).</summary>
public class RegenerateRecoveryCodesHandler(
    UserManager<ApplicationUser> userManager,
    ITwoFactorService twoFactor,
    IIdentityAuditWriter audit,
    IIdentityUnitOfWork unitOfWork,
    ILogger<RegenerateRecoveryCodesHandler> logger)
    : ICommandHandler<RegenerateRecoveryCodesCommand, RegenerateRecoveryCodesResult>
{
    public async Task<RegenerateRecoveryCodesResult> Handle(
        RegenerateRecoveryCodesCommand command,
        CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UnauthorizedException("Not authenticated");

        if (!user.IsActive)
        {
            throw new UnauthorizedException("Account is disabled.");
        }

        if (!await userManager.GetTwoFactorEnabledAsync(user))
        {
            throw new BadRequestException("Two-factor authentication is not enabled.");
        }

        var factor = await twoFactor.VerifySecondFactorAsync(
            user, command.Code, recoveryCode: null, allowRecoveryCode: false);
        if (factor == SecondFactorResult.Failed)
        {
            throw new BadRequestException("The authenticator code is invalid or expired.");
        }

        var codes = await unitOfWork.ExecuteAsync(async () =>
        {
            var issued = await twoFactor.GenerateRecoveryCodesAsync(user);
            audit.Record(IdentityAuditActions.TwoFactorRecoveryCodesRegenerated, user.Id,
                after: new { RecoveryCodeCount = issued.Count }, actorId: user.Id);
            return issued;
        }, cancellationToken);

        logger.LogInformation("User {UserId} regenerated their recovery codes", user.Id);

        return new RegenerateRecoveryCodesResult(
            codes, "New recovery codes issued; the previous ones no longer work. They are shown only once.");
    }
}
