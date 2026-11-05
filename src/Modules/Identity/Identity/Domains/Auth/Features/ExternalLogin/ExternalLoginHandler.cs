using Identity.Contracts.Auth.Commands;
using Identity.Services.Sessions;
using Identity.Services.TwoFactor;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Features.ExternalLogin;

public class ExternalLoginHandler(ILogger<ExternalLoginHandler> logger)
    : ICommandHandler<ExternalLoginCommand, ExternalLoginResult>
{
    public Task<ExternalLoginResult> Handle(ExternalLoginCommand command, CancellationToken cancellationToken)
    {
        logger.LogInformation("Initiating external login with {Provider}", command.Provider);

        // The frontend redirects the browser to this challenge URL.
        return Task.FromResult(new ExternalLoginResult(
            $"/api/identity/auth/challenge/{Uri.EscapeDataString(command.Provider)}" +
            $"?returnUrl={Uri.EscapeDataString(command.ReturnUrl)}"));
    }
}

public class ExternalLoginCallbackHandler(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IAuthSessionService sessionService,
    ITwoFactorChallengeService challengeService,
    IHttpContextService httpContextService,
    Identity.Services.Sso.ISsoEnforcementPolicy ssoPolicy,
    ILogger<ExternalLoginCallbackHandler> logger)
    : ICommandHandler<ExternalLoginCallbackCommand, ExternalLoginCallbackResult>
{
    public async Task<ExternalLoginCallbackResult> Handle(
        ExternalLoginCallbackCommand command,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(command.RemoteError))
        {
            logger.LogError("Error from external provider: {Error}", command.RemoteError);
            throw new BadRequestException("Error from external provider.");
        }

        var info = await signInManager.GetExternalLoginInfoAsync()
                   ?? throw new BadRequestException("Failed to load external login information");

        // Never bypass 2FA: an external provider is only the FIRST factor.
        var result = await signInManager.ExternalLoginSignInAsync(
            info.LoginProvider,
            info.ProviderKey,
            isPersistent: false,
            bypassTwoFactor: false);

        if (result.Succeeded || result.RequiresTwoFactor)
        {
            var user = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey)
                       ?? throw new NotFoundException("User", info.ProviderKey);

            if (!user.IsActive)
            {
                throw new UnauthorizedException("Account is disabled.");
            }

            // A social login does not get around an organization's SSO enforcement.
            await ssoPolicy.EnsureNonSsoSignInAllowedAsync(user, cancellationToken);

            // Also covers a "remembered" 2FA client cookie making SignInManager report Succeeded: tokens are only
            // ever issued after our own second-factor step.
            if (result.RequiresTwoFactor || await userManager.GetTwoFactorEnabledAsync(user))
            {
                var challenge = challengeService.Create(user);
                logger.LogInformation("User {UserId} passed {Provider}; two-factor challenge issued",
                    user.Id, info.LoginProvider);

                return new ExternalLoginCallbackResult(
                    user.Id, false, false, null, null, null, "Two-factor authentication required")
                {
                    RequiresTwoFactor = true,
                    TwoFactorChallengeToken = challenge.Token,
                    TwoFactorChallengeExpiresAt = challenge.ExpiresAt
                };
            }

            if (!user.IsPhoneNumberVerified)
            {
                return new ExternalLoginCallbackResult(
                    user.Id, false, true, null, null, null,
                    "Please verify your phone number to complete registration");
            }

            // Persisted, rotating refresh token (the old code returned an unsaved one that could never refresh).
            var session = await sessionService.IssueAsync(user, httpContextService.GetIpAddress(), cancellationToken);

            user.LastLoginAt = DateTime.UtcNow;
            await userManager.UpdateAsync(user);

            logger.LogInformation("User {UserId} logged in with {Provider}", user.Id, info.LoginProvider);

            return new ExternalLoginCallbackResult(
                user.Id, false, false,
                session.AccessToken, session.RefreshToken, session.AccessTokenExpiresAt,
                "Login successful");
        }

        if (result.IsLockedOut)
        {
            throw new BadRequestException("User account is locked out");
        }

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(email))
        {
            throw new BadRequestException("Email claim not received from external provider");
        }

        var existingUser = await userManager.FindByEmailAsync(email);
        ApplicationUser newUser;

        if (existingUser != null)
        {
            // Linking a social login must not open a back door around an organization that enforces SSO.
            await ssoPolicy.EnsureNonSsoSignInAllowedAsync(existingUser, cancellationToken);

            var addLoginResult = await userManager.AddLoginAsync(existingUser, info);
            if (!addLoginResult.Succeeded)
            {
                var errors = string.Join(", ", addLoginResult.Errors.Select(e => e.Description));
                throw new BadRequestException($"Failed to link external login: {errors}");
            }

            newUser = existingUser;
        }
        else
        {
            // A new account on an SSO-enforced domain must come from the IdP (JIT), not from a social provider.
            if (await ssoPolicy.FindConnectionForEmailAsync(email, cancellationToken) is { EnforceSso: true } enforcing)
                throw new Identity.Domains.Sso.Exceptions.SsoRequiredException(enforcing.Id);

            newUser = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = email,
                Email = email,
                EmailConfirmed = true,
                FirstName = info.Principal.FindFirstValue(ClaimTypes.GivenName),
                LastName = info.Principal.FindFirstValue(ClaimTypes.Surname),
                OAuthProvider = info.LoginProvider,
                OAuthProviderId = info.ProviderKey,
                IsPhoneNumberVerified = false,
                IsProfileComplete = false,
                CreatedAt = DateTime.UtcNow
            };

            var createResult = await userManager.CreateAsync(newUser);
            if (!createResult.Succeeded)
            {
                var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                throw new BadRequestException($"Failed to create user: {errors}");
            }

            var addLoginResult = await userManager.AddLoginAsync(newUser, info);
            if (!addLoginResult.Succeeded)
            {
                var errors = string.Join(", ", addLoginResult.Errors.Select(e => e.Description));
                throw new BadRequestException($"Failed to add external login: {errors}");
            }

            await userManager.AddToRoleAsync(newUser, RoleConstants.User);

            logger.LogInformation("New user {UserId} created via {Provider}", newUser.Id, info.LoginProvider);
        }

        await signInManager.SignInAsync(newUser, isPersistent: false, info.LoginProvider);

        return new ExternalLoginCallbackResult(
            newUser.Id,
            existingUser == null,
            true,
            null,
            null,
            null,
            "Registration successful. Please verify your phone number");
    }
}
