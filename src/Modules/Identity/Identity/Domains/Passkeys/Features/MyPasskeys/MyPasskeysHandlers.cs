using Identity.Contracts.Passkeys;
using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Domains.Passkeys.Exceptions;
using Identity.Services.Passkeys;
using Identity.Services.Security;
using Notification.Contracts.Email.Models;

namespace Identity.Domains.Passkeys.Features.MyPasskeys;

public class BeginPasskeyRegistrationCommandValidator : AbstractValidator<BeginPasskeyRegistrationCommand>
{
    public BeginPasskeyRegistrationCommandValidator() => RuleFor(x => x.UserId).NotEmpty();
}

/// <summary>Step 1 of adding a passkey: step-up check, limit check, WebAuthn creation options.</summary>
public class BeginPasskeyRegistrationHandler(
    UserManager<ApplicationUser> userManager,
    IPasskeyService passkeys) : ICommandHandler<BeginPasskeyRegistrationCommand, PasskeyOptionsResult>
{
    public async Task<PasskeyOptionsResult> Handle(BeginPasskeyRegistrationCommand command, CancellationToken cancellationToken)
    {
        passkeys.EnsureEnabled();
        var user = await PasskeyUsers.LoadActiveAsync(userManager, command.UserId);
        await passkeys.EnsureFreshSessionAsync(user.Id, cancellationToken);
        return await passkeys.BeginRegistrationAsync(user, cancellationToken);
    }
}

public class CompletePasskeyRegistrationCommandValidator : AbstractValidator<CompletePasskeyRegistrationCommand>
{
    public CompletePasskeyRegistrationCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.CeremonyId).NotEmpty().MaximumLength(64);
        RuleFor(x => x.CredentialJson).NotEmpty().MaximumLength(64 * 1024);
        RuleFor(x => x.Name).MaximumLength(100);
    }
}

/// <summary>
/// Step 2: verifies the attestation and stores the passkey. Audited (same transaction) and announced by a security
/// email, so a credential planted from a hijacked session does not go unnoticed. Existing sessions stay valid.
/// </summary>
public class CompletePasskeyRegistrationHandler(
    UserManager<ApplicationUser> userManager,
    IPasskeyService passkeys,
    IIdentityAuditWriter audit,
    ISecurityAlertSender securityAlerts,
    ILogger<CompletePasskeyRegistrationHandler> logger) : ICommandHandler<CompletePasskeyRegistrationCommand, PasskeyDto>
{
    public async Task<PasskeyDto> Handle(CompletePasskeyRegistrationCommand command, CancellationToken cancellationToken)
    {
        var user = await PasskeyUsers.LoadActiveAsync(userManager, command.UserId);

        var passkey = await passkeys.CompleteRegistrationAsync(
            user, command.CeremonyId, command.CredentialJson, command.Name, cancellationToken);

        var dto = PasskeyMapping.ToDto(passkey);
        audit.Record(IdentityAuditActions.PasskeyAdded, user.Id, actorId: user.Id,
            after: new { dto.Id, dto.Name, dto.IsBackedUp, dto.Transports });
        await securityAlerts.SendAsync(user, SecurityAlertEvents.PasskeyAdded, dto.Name ?? "Passkey", cancellationToken);

        logger.LogInformation("User {UserId} added a passkey", user.Id);
        return dto;
    }
}

public class GetMyPasskeysHandler(
    UserManager<ApplicationUser> userManager,
    IPasskeyService passkeys) : IQueryHandler<GetMyPasskeysQuery, List<PasskeyDto>>
{
    public async Task<List<PasskeyDto>> Handle(GetMyPasskeysQuery query, CancellationToken cancellationToken)
    {
        passkeys.EnsureEnabled();
        var user = await userManager.FindByIdAsync(query.UserId.ToString())
                   ?? throw new UnauthorizedException("Not authenticated");

        var all = await userManager.GetPasskeysAsync(user);
        return all.OrderByDescending(p => p.CreatedAt).Select(PasskeyMapping.ToDto).ToList();
    }
}

public class RenamePasskeyCommandValidator : AbstractValidator<RenamePasskeyCommand>
{
    public RenamePasskeyCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.PasskeyId).NotEmpty().MaximumLength(1366);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
    }
}

public class RenamePasskeyHandler(
    UserManager<ApplicationUser> userManager,
    IPasskeyService passkeys,
    IIdentityAuditWriter audit) : ICommandHandler<RenamePasskeyCommand, PasskeyDto>
{
    public async Task<PasskeyDto> Handle(RenamePasskeyCommand command, CancellationToken cancellationToken)
    {
        passkeys.EnsureEnabled();
        var user = await PasskeyUsers.LoadActiveAsync(userManager, command.UserId);
        var passkey = await PasskeyUsers.FindOwnAsync(userManager, user, command.PasskeyId);

        var before = passkey.Name;
        passkey.Name = PasskeyService.NormalizeName(command.Name);

        var saved = await userManager.AddOrUpdatePasskeyAsync(user, passkey);
        if (!saved.Succeeded)
            throw new InvalidOperationException($"Failed to rename the passkey: {string.Join(", ", saved.Errors.Select(e => e.Code))}");

        audit.Record(IdentityAuditActions.PasskeyRenamed, user.Id, actorId: user.Id,
            before: new { Name = before }, after: new { passkey.Name });

        return PasskeyMapping.ToDto(passkey);
    }
}

public class DeletePasskeyCommandValidator : AbstractValidator<DeletePasskeyCommand>
{
    public DeletePasskeyCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.PasskeyId).NotEmpty().MaximumLength(1366);
    }
}

/// <summary>
/// Removes a passkey. Refused when it is the last way into the account (no password, no other passkey, no linked
/// external or SSO login): recovery would otherwise need support.
/// </summary>
public class DeletePasskeyHandler(
    UserManager<ApplicationUser> userManager,
    IPasskeyService passkeys,
    IIdentityAuditWriter audit,
    ISecurityAlertSender securityAlerts) : ICommandHandler<DeletePasskeyCommand, DeletePasskeyResult>
{
    public async Task<DeletePasskeyResult> Handle(DeletePasskeyCommand command, CancellationToken cancellationToken)
    {
        passkeys.EnsureEnabled();
        var user = await PasskeyUsers.LoadActiveAsync(userManager, command.UserId);
        var passkey = await PasskeyUsers.FindOwnAsync(userManager, user, command.PasskeyId);

        var others = (await userManager.GetPasskeysAsync(user))
            .Count(p => !p.CredentialId.AsSpan().SequenceEqual(passkey.CredentialId));
        var hasPassword = await userManager.HasPasswordAsync(user);
        var hasExternalLogin = (await userManager.GetLoginsAsync(user)).Count > 0;

        if (others == 0 && !hasPassword && !hasExternalLogin)
            throw new LastSignInMethodException();

        var removed = await userManager.RemovePasskeyAsync(user, passkey.CredentialId);
        if (!removed.Succeeded)
            throw new InvalidOperationException($"Failed to remove the passkey: {string.Join(", ", removed.Errors.Select(e => e.Code))}");

        var dto = PasskeyMapping.ToDto(passkey);
        audit.Record(IdentityAuditActions.PasskeyRemoved, user.Id, actorId: user.Id, before: new { dto.Id, dto.Name });
        await securityAlerts.SendAsync(user, SecurityAlertEvents.PasskeyRemoved, dto.Name ?? "Passkey", cancellationToken);

        return new DeletePasskeyResult(true);
    }
}

internal static class PasskeyUsers
{
    public static async Task<ApplicationUser> LoadActiveAsync(UserManager<ApplicationUser> userManager, Guid userId)
    {
        var user = await userManager.FindByIdAsync(userId.ToString())
                   ?? throw new UnauthorizedException("Not authenticated");
        if (!user.IsActive)
            throw new UnauthorizedException("Account is disabled.");
        return user;
    }

    /// <summary>Scoped to the caller: another user's credential id looks exactly like an unknown one (404).</summary>
    public static async Task<UserPasskeyInfo> FindOwnAsync(
        UserManager<ApplicationUser> userManager, ApplicationUser user, string passkeyId)
    {
        var credentialId = PasskeyService.DecodeId(passkeyId) ?? throw new PasskeyNotFoundException(passkeyId);
        return await userManager.GetPasskeyAsync(user, credentialId) ?? throw new PasskeyNotFoundException(passkeyId);
    }
}

internal static class PasskeyMapping
{
    public static PasskeyDto ToDto(UserPasskeyInfo p) => new(
        PasskeyService.EncodeId(p.CredentialId),
        p.Name,
        p.CreatedAt.UtcDateTime,
        p.IsBackedUp,
        p.IsBackupEligible,
        p.IsUserVerified,
        p.Transports ?? []);
}
