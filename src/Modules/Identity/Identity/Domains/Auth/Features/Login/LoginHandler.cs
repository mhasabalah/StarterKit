using Identity.Contracts.Auth.Commands;
using Identity.Contracts.Auth.Dtos;
using Identity.Data;
using Identity.Services.Token;
using Identity.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Identity.Domains.Auth.Features.Login;

public class LoginCommandValidator : AbstractValidator<LoginCommand>
{
    public LoginCommandValidator()
    {
        RuleFor(x => x.Identifier).NotEmpty()
            .WithMessage("Email or phone number is required");
        RuleFor(x => x.Password).NotEmpty()
            .WithMessage("Password is required");
    }
}

public class LoginHandler(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IJwtTokenGenerator tokenGenerator,
    ApplicationIdentityDbContext dbContext,
    IHttpContextAccessor httpContextAccessor,
    IDistributedCache cache,
    IOptions<LoginPolicyOptions> loginPolicyOptions,
    IOptions<JwtOptions> jwtOptions,
    ILogger<LoginHandler> logger)
    : ICommandHandler<LoginCommand, LoginResult>
{
    private readonly LoginPolicyOptions _loginPolicy = loginPolicyOptions.Value;
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    public async Task<LoginResult> Handle(LoginCommand command, CancellationToken cancellationToken)
    {
        var user = await FindUserByIdentifier(command.Identifier);

        if (user == null)
        {
            logger.LogWarning("Login attempt failed: User not found for identifier {Identifier}",
                command.Identifier);
            throw new UnauthorizedException("Invalid credentials");
        }

        var result = await signInManager.CheckPasswordSignInAsync(
            user,
            command.Password,
            true);

        if (!result.Succeeded)
        {
            if (result.IsLockedOut)
            {
                logger.LogWarning("Login attempt failed: Account locked out for {Identifier}",
                    command.Identifier);
                throw new UnauthorizedException("Account is locked out. Please try again later.");
            }

            logger.LogWarning("Login attempt failed: Invalid password for {Identifier}",
                command.Identifier);
            throw new UnauthorizedException("Invalid credentials");
        }

        var roles = await userManager.GetRolesAsync(user);
        var userDto = CreateUserDto(user, roles);

        var (shouldBlockLogin, blockReason) = CheckVerificationRequirements(user);

        if (shouldBlockLogin && _loginPolicy.AllowPartialLogin)
        {
            logger.LogWarning("User {UserId} logged in but {Reason}", user.Id, blockReason);

            return new LoginResult(
                null,
                null,
                null,
                userDto,
                blockReason
            );
        }

        // Generate tokens
        var accessToken = await tokenGenerator.GenerateTokenAsync(user);
        var refreshTokenString = tokenGenerator.GenerateRefreshToken();
        var refreshTokenHash = ComputeTokenHash(refreshTokenString);
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes);

        // Get IP address for audit trail
        var ipAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
            ?? "unknown";

        // Store refresh token in database
        var refreshToken = new Models.RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = user.Id,
            TokenHash = refreshTokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpirationDays),
            CreatedAt = DateTime.UtcNow,
            CreatedByIp = ipAddress
        };

        dbContext.RefreshTokens.Add(refreshToken);

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await userManager.UpdateAsync(user);

        // Save changes
        await dbContext.SaveChangesAsync(cancellationToken);

        var message = shouldBlockLogin
            ? $"Login successful. {blockReason}"
            : "Login successful";

        logger.LogInformation(
            "User {UserId} ({Identifier}) logged in successfully from IP {IpAddress}",
            user.Id,
            command.Identifier,
            ipAddress);

        return new LoginResult(
            accessToken,
            refreshTokenString,  // Return raw token to client (not hash)
            expiresAt,
            userDto,
            message
        );
    }

    private (bool ShouldBlock, string? Reason) CheckVerificationRequirements(ApplicationUser user)
    {
        if (_loginPolicy.RequireEmailVerification && !user.EmailConfirmed)
        {
            return (true, "Please verify your email address to access all features.");
        }

        if (_loginPolicy.RequirePhoneVerification && !user.IsPhoneNumberVerified)
        {
            return (true, "Please verify your phone number to access all features.");
        }

        if (_loginPolicy.RequireProfileCompletion && !user.IsProfileComplete)
        {
            return (true, "Please complete your profile to access all features.");
        }

        return (false, null);
    }

    private static UserDto CreateUserDto(ApplicationUser user, IList<string> roles)
    {
        return new UserDto
        {
            Id = user.Id,
            Email = user.Email,
            PhoneNumber = user.PhoneNumber,
            FirstName = user.FirstName,
            LastName = user.LastName,
            IsEmailVerified = user.EmailConfirmed,
            IsPhoneVerified = user.IsPhoneNumberVerified,
            IsProfileComplete = user.IsProfileComplete,
            Roles = roles.ToList()
        };
    }

    private async Task<ApplicationUser?> FindUserByIdentifier(string identifier)
    {
        var user = await userManager.FindByEmailAsync(identifier);
        if (user != null)
        {
            return user;
        }

        user = await userManager.Users
            .FirstOrDefaultAsync(u => u.PhoneNumber == identifier);

        return user;
    }

    /// <summary>
    /// Compute SHA256 hash of token for secure storage
    /// </summary>
    private static string ComputeTokenHash(string token)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashBytes);
    }
}
