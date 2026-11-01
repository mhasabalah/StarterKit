using Identity.Contracts.Auth.Commands;
using Identity.Services.Token;
using Microsoft.AspNetCore.Authentication;

namespace Identity.Domains.Auth.Features.ExternalLogin;

public class ExternalLoginHandler(
    SignInManager<ApplicationUser> signInManager,
    ILogger<ExternalLoginHandler> logger) 
    : ICommandHandler<ExternalLoginCommand, ExternalLoginResult>
{
    public Task<ExternalLoginResult> Handle(ExternalLoginCommand command, CancellationToken cancellationToken)
    {
        var redirectUrl = $"/api/identity/auth/external-callback?returnUrl={command.ReturnUrl}";
        var properties = signInManager.ConfigureExternalAuthenticationProperties(command.Provider, redirectUrl);
        
        logger.LogInformation("Initiating external login with {Provider}", command.Provider);
        
        // Return challenge URL that frontend should redirect to
        return Task.FromResult(new ExternalLoginResult(
            $"/api/identity/auth/challenge/{command.Provider}?returnUrl={command.ReturnUrl}"
        ));
    }
}

public class ExternalLoginCallbackHandler(
    SignInManager<ApplicationUser> signInManager,
    UserManager<ApplicationUser> userManager,
    IJwtTokenGenerator tokenGenerator,
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
            throw new BadRequestException($"Error from external provider: {command.RemoteError}");
        }

        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info == null)
        {
            logger.LogError("Failed to load external login information");
            throw new BadRequestException("Failed to load external login information");
        }

        // Sign in the user with this external login provider if the user already has a login
        var result = await signInManager.ExternalLoginSignInAsync(
            info.LoginProvider, 
            info.ProviderKey, 
            isPersistent: false, 
            bypassTwoFactor: true);

        if (result.Succeeded)
        {
            var user = await userManager.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
            if (user == null)
            {
                throw new NotFoundException("User", info.ProviderKey);
            }

            // Check if phone verified
            if (!user.IsPhoneNumberVerified)
            {
                return new ExternalLoginCallbackResult(
                    user.Id,
                    false,
                    true,
                    null,
                    null,
                    null,
                    "Please verify your phone number to complete registration"
                );
            }

            // Generate JWT tokens
            var accessToken = await tokenGenerator.GenerateTokenAsync(user);
            var refreshToken = tokenGenerator.GenerateRefreshToken();
            
            user.LastLoginAt = DateTime.UtcNow;
            await userManager.UpdateAsync(user);

            logger.LogInformation("User {Email} logged in with {Provider}", user.Email, info.LoginProvider);

            return new ExternalLoginCallbackResult(
                user.Id,
                false,
                false,
                accessToken,
                refreshToken,
                DateTime.UtcNow.AddHours(1),
                "Login successful"
            );
        }

        if (result.IsLockedOut)
        {
            throw new BadRequestException("User account is locked out");
        }

        // If the user does not have an account, then create one
        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(email))
        {
            throw new BadRequestException("Email claim not received from external provider");
        }

        var existingUser = await userManager.FindByEmailAsync(email);
        ApplicationUser newUser;

        if (existingUser != null)
        {
            // User exists but doesn't have this external login linked
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
            // Create new user
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

            await userManager.AddToRoleAsync(newUser, "User");

            logger.LogInformation("New user {Email} created via {Provider}", email, info.LoginProvider);
        }

        await signInManager.SignInAsync(newUser, isPersistent: false, info.LoginProvider);

        return new ExternalLoginCallbackResult(
            newUser.Id,
            existingUser == null,
            true,
            null,
            null,
            null,
            "Registration successful. Please verify your phone number"
        );
    }
}
