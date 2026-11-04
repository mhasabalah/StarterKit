using Identity.Contracts.Users.Commands;
using Identity.Domains.Audit.Services;
using Identity.Domains.Users.Services;
using Identity.Resources;
using Identity.Services.Otp;
using Identity.Services.Sessions;
using Microsoft.Extensions.Localization;
using Tenancy.Contracts.Tenants;

namespace Identity.Domains.Users.Features.DeleteMyAccount;

public class DeleteMyAccountCommandValidator : AbstractValidator<DeleteMyAccountCommand>
{
    public DeleteMyAccountCommandValidator(IStringLocalizer<IdentityResource> localizer)
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x)
            .Must(x => !string.IsNullOrEmpty(x.Password) || !string.IsNullOrEmpty(x.OtpCode))
            .WithMessage(_ => localizer["Account.Delete.ReauthRequired"]);
    }
}

/// <summary>
/// Self-service erasure. Re-authenticates (password, or SMS code for password-less accounts), refuses while the user
/// owns a tenant or holds a privileged role, then ends every session and anonymizes the user IN PLACE: the row stays
/// (referential integrity, financial records) but carries no personal data and can never sign in. The
/// <see cref="AccountDeletion"/> tombstone (and its outbox row) and the audit entry are saved in the same
/// <c>SaveChanges</c> as the anonymized user, so other modules erase their data (<c>UserDeletedIntegrationEvent</c>)
/// if and only if the erasure committed. Everything runs in ONE transaction (<see cref="IIdentityUnitOfWork"/>): a failure
/// rolls it all back, leaving the account intact and usable so the request can simply be repeated.
/// </summary>
public class DeleteMyAccountHandler(
    UserManager<ApplicationUser> userManager,
    ApplicationIdentityDbContext dbContext,
    IOtpCodeService otpCodeService,
    ISessionRevoker sessionRevoker,
    ISender sender,
    IIdentityAuditWriter audit,
    IIdentityUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IStringLocalizer<IdentityResource> localizer,
    ILogger<DeleteMyAccountHandler> logger)
    : ICommandHandler<DeleteMyAccountCommand, DeleteMyAccountResult>
{
    public async Task<DeleteMyAccountResult> Handle(DeleteMyAccountCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UnauthorizedException("Not authenticated");
        if (!user.IsActive) throw new UnauthorizedException("Account is disabled.");

        var roles = await userManager.GetRolesAsync(user);
        if (RoleHierarchy.IsAdminOrAbove(roles))
            throw new ConflictException(localizer["Account.Delete.PrivilegedAccount"]);

        await ReauthenticateAsync(user, command, cancellationToken);

        var owned = await sender.Send(new GetOwnedTenantsQuery(user.Id), cancellationToken);
        if (owned.Count > 0)
            throw new ConflictException(
                localizer["Account.Delete.OwnsTenants", string.Join(", ", owned.Select(t => t.Identifier))]);

        // One transaction: session revocation, the bulk deletes, the anonymizing update, the tombstone (+ outbox row)
        // and the audit entry commit together or not at all, so a failure never leaves a half-erased usable account.
        await unitOfWork.ExecuteAsync(async () =>
        {
            // End every session first (rotates the stamp, revokes refresh + access tokens).
            await sessionRevoker.RevokeAllAsync(user.Id, "Account deleted by user", cancellationToken);

            // Personal data held in rows of their own.
            await dbContext.UserLogins.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
            await dbContext.UserClaims.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
            await dbContext.UserRoles.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
            await dbContext.UserTokens.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(cancellationToken); // authenticator key, recovery codes
            await dbContext.OtpCodes.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(cancellationToken);
            await dbContext.RefreshTokens.Where(x => x.UserId == user.Id).ExecuteDeleteAsync(cancellationToken); // incl. IPs

            var now = timeProvider.GetUtcNow().UtcDateTime;
            Anonymize(user, now);

            dbContext.AccountDeletions.Add(AccountDeletion.Create(user.Id, now));
            audit.RecordAccountErasure(user.Id);

            var result = await userManager.UpdateAsync(user);
            if (!result.Succeeded)
                throw new InvalidOperationException(
                    $"Could not anonymize user {user.Id}: {string.Join("; ", result.Errors.Select(e => e.Code))}");

            return true;
        }, cancellationToken);

        logger.LogInformation("User {UserId} erased their account", user.Id);
        return new DeleteMyAccountResult(localizer["Account.Delete.Done"]);
    }

    private async Task ReauthenticateAsync(ApplicationUser user, DeleteMyAccountCommand command, CancellationToken ct)
    {
        if (await userManager.HasPasswordAsync(user))
        {
            if (string.IsNullOrEmpty(command.Password))
                throw new BadRequestException(localizer["Account.Delete.PasswordRequired"]);

            if (!await userManager.CheckPasswordAsync(user, command.Password))
            {
                await userManager.AccessFailedAsync(user); // counts toward lockout like a failed login
                throw new BadRequestException(localizer["Account.Delete.WrongPassword"]);
            }

            return;
        }

        if (string.IsNullOrEmpty(command.OtpCode))
            throw new BadRequestException(localizer["Account.Delete.CodeRequired"]);

        var verification = await otpCodeService.VerifyAsync(user.Id, OtpPurpose.AccountDeletion, command.OtpCode, ct);
        if (!verification.Succeeded)
            throw new BadRequestException(localizer["Account.Delete.WrongCode"]);
    }

    /// <summary>Removes every personal field; the user can no longer sign in, reset a password or be contacted.</summary>
    private static void Anonymize(ApplicationUser user, DateTime now)
    {
        var token = user.Id.ToString("N");

        user.UserName = $"deleted-{token}";
        user.Email = $"deleted-{token}@deleted.invalid";
        user.EmailConfirmed = false;
        user.PhoneNumber = null;
        user.PhoneNumberConfirmed = false;
        user.IsPhoneNumberVerified = false;
        user.FirstName = "Deleted";
        user.LastName = "User";
        user.ProfilePictureUrl = null;
        user.OAuthProvider = null;
        user.OAuthProviderId = null;
        user.LastActiveTenantId = null;
        user.PasswordHash = null;
        user.TwoFactorEnabled = false;
        user.MustChangePasswordOnNextLogin = false;
        user.IsActive = false;
        user.LockoutEnabled = true;
        user.LockoutEnd = DateTimeOffset.MaxValue;
        user.UpdatedAt = now;
    }
}
