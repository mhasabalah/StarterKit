using Identity.Contracts.Auth.Commands;
using Identity.Services.Sessions;
using Shared.Core.Authorization;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Features.SwitchTenant;

public class SwitchTenantCommandValidator : AbstractValidator<SwitchTenantCommand>
{
    public SwitchTenantCommandValidator() => RuleFor(x => x.TenantId).NotEmpty();
}

/// <summary>
/// Re-issues the caller's session for another tenant. The 2FA state (<c>amr=mfa</c>) of the current access token is
/// carried over; membership and tenant status are checked by <see cref="IAuthSessionService.SwitchTenantAsync"/>.
/// </summary>
public class SwitchTenantHandler(
    UserManager<ApplicationUser> userManager,
    IAuthSessionService sessionService,
    IHttpContextService httpContextService)
    : ICommandHandler<SwitchTenantCommand, SwitchTenantResult>
{
    public async Task<SwitchTenantResult> Handle(SwitchTenantCommand command, CancellationToken cancellationToken)
    {
        var userId = httpContextService.GetUserId() ?? throw new UnauthorizedException("Authentication is required.");
        var user = await userManager.FindByIdAsync(userId);
        if (user is not { IsActive: true }) throw new UnauthorizedException("Authentication is required.");

        // JwtBearer maps inbound "amr" to its long claim type: IsMultiFactor checks both (a raw "amr" check is always false).
        var multiFactor = AuthenticationMethods.IsMultiFactor(httpContextService.GetAllClaims());
        var session = await sessionService.SwitchTenantAsync(user, command.TenantId,
            httpContextService.GetIpAddress(), multiFactor, cancellationToken);

        return new SwitchTenantResult(session.AccessToken, session.RefreshToken, session.AccessTokenExpiresAt,
            command.TenantId);
    }
}
