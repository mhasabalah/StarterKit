using Identity.Contracts.Auth.Commands;
using Identity.Contracts.Auth.Dtos;
using Identity.Data;
using Identity.Domains.Roles.Constants;
using Identity.Options;
using Identity.Resources;
using Identity.Services.Token;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;
using Shared.Core.Phone;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Identity.Domains.Auth.Features.Register;

public class RegisterCommandValidator : AbstractValidator<RegisterCommand>
{
    private readonly IPhoneValidationService _phoneService;

    public RegisterCommandValidator(IStringLocalizer<IdentityResource> localizer, IPhoneValidationService phoneService)
    {
        _phoneService = phoneService;

        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage(_ => localizer["Validation.Identifier.Required"])
            .Must(BeValidIdentifier).WithMessage(_ => localizer["Validation.Identifier.Invalid"]);

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage(_ => localizer["Validation.Password.Required"])
            .MinimumLength(8).WithMessage(_ => localizer["Validation.Password.MinLength"])
            .Matches(@"[A-Z]").WithMessage(_ => localizer["Validation.Password.Uppercase"])
            .Matches(@"[a-z]").WithMessage(_ => localizer["Validation.Password.Lowercase"])
            .Matches(@"[0-9]").WithMessage(_ => localizer["Validation.Password.Digit"])
            .Matches(@"[\W_]").WithMessage(_ => localizer["Validation.Password.SpecialChar"]);

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password).WithMessage(_ => localizer["Validation.ConfirmPassword.Match"]);
    }

    private bool BeValidIdentifier(string identifier)
    {
        return IsValidEmail(identifier) || IsValidPhone(identifier);
    }

    private bool IsValidEmail(string email)
    {
        return Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
    }

    private bool IsValidPhone(string phone)
    {
        return _phoneService.Validate(phone, "ZZ").IsValid;
    }
}

public class RegisterHandler(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IJwtTokenGenerator tokenGenerator,
    ApplicationIdentityDbContext dbContext,
    IHttpContextAccessor httpContextAccessor,
    IOptions<JwtOptions> jwtOptions,
    IStringLocalizer<IdentityResource> localizer,
    ILogger<RegisterHandler> logger)
    : ICommandHandler<RegisterCommand, RegisterResult>
{
    private readonly JwtOptions _jwtOptions = jwtOptions.Value;

    public async Task<RegisterResult> Handle(RegisterCommand command,
        CancellationToken cancellationToken)
    {
        var identifierType = DetectIdentifierType(command.Identifier);

        return identifierType switch
        {
            IdentifierType.Email => await RegisterWithEmail(command, cancellationToken),
            IdentifierType.Phone => await RegisterWithPhone(command, cancellationToken),
            _ => throw new BadRequestException(localizer["Auth.InvalidIdentifier"])
        };
    }

    private IdentifierType DetectIdentifierType(string identifier)
    {
        if (identifier.Contains("@"))
        {
            return IdentifierType.Email;
        }

        if (identifier.StartsWith("+") || Regex.IsMatch(identifier, @"^\d{10,}$"))
        {
            return IdentifierType.Phone;
        }

        throw new BadRequestException(localizer["Auth.InvalidIdentifier"]);
    }

    private async Task<RegisterResult> RegisterWithEmail(RegisterCommand command,
        CancellationToken cancellationToken)
    {
        var existingUser = await userManager.FindByEmailAsync(command.Identifier);
        if (existingUser != null)
        {
            throw new BadRequestException(localizer["Auth.Register.EmailExists"]);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = command.Identifier,
            Email = command.Identifier,
            FirstName = command.FirstName,
            LastName = command.LastName,
            EmailConfirmed = false,
            IsPhoneNumberVerified = false,
            IsProfileComplete = false,
            CreatedAt = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(user, command.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new BadRequestException(localizer["Auth.Register.Failed", errors]);
        }

        // Assign default User role
        await EnsureRoleExistsAndAssignAsync(user, RoleConstants.User);

        var tokens = await GenerateAndSaveTokens(user, cancellationToken);

        logger.LogInformation("User registered with email: {Email}", command.Identifier);

        var roles = await userManager.GetRolesAsync(user);
        return new RegisterResult(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.ExpiresAt,
            CreateUserDto(user, roles),
            localizer["Auth.Register.Success"]
        );
    }

    private async Task<RegisterResult> RegisterWithPhone(RegisterCommand command,
        CancellationToken cancellationToken)
    {
        var existingUser = await userManager.Users
            .FirstOrDefaultAsync(u => u.PhoneNumber == command.Identifier);

        if (existingUser != null)
        {
            throw new BadRequestException(localizer["Auth.Register.PhoneExists"]);
        }

        // Generate placeholder email since Identity requires unique email
        var placeholderEmail = $"{command.Identifier.Replace("+", "")}{ApplicationUser.PhonePlaceholderEmailDomain}";

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = command.Identifier,
            Email = placeholderEmail, // Placeholder email for phone-based registration
            PhoneNumber = command.Identifier,
            FirstName = command.FirstName,
            LastName = command.LastName,
            EmailConfirmed = false,
            IsPhoneNumberVerified = false,
            IsProfileComplete = false,
            CreatedAt = DateTime.UtcNow
        };

        var result = await userManager.CreateAsync(user, command.Password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new BadRequestException(localizer["Auth.Register.Failed", errors]);
        }

        // Assign default User role
        await EnsureRoleExistsAndAssignAsync(user, RoleConstants.User);

        var tokens = await GenerateAndSaveTokens(user, cancellationToken);

        logger.LogInformation("User registered with phone: {Phone}", command.Identifier);

        var roles = await userManager.GetRolesAsync(user);
        return new RegisterResult(
            tokens.AccessToken,
            tokens.RefreshToken,
            tokens.ExpiresAt,
            CreateUserDto(user, roles),
            localizer["Auth.Register.Success"]
        );
    }

    private async Task<(string AccessToken, string RefreshToken, DateTime ExpiresAt)>
        GenerateAndSaveTokens(ApplicationUser user, CancellationToken cancellationToken)
    {
        var accessToken = await tokenGenerator.GenerateTokenAsync(user);
        var refreshTokenString = tokenGenerator.GenerateRefreshToken();
        var refreshTokenHash = ComputeTokenHash(refreshTokenString);
        var expiresAt = DateTime.UtcNow.AddMinutes(_jwtOptions.AccessTokenExpirationMinutes);

        var ipAddress = httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString()
                        ?? "unknown";

        // Persist refresh token — same pattern as LoginHandler
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
        await dbContext.SaveChangesAsync(cancellationToken);

        return (accessToken, refreshTokenString, expiresAt);
    }

    private static string ComputeTokenHash(string token)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(hashBytes);
    }

    /// <summary>
    /// Ensure role exists before assigning it to user
    /// </summary>
    private async Task EnsureRoleExistsAndAssignAsync(ApplicationUser user, string roleName)
    {
        // Check if role exists
        var roleExists = await roleManager.RoleExistsAsync(roleName);

        if (!roleExists)
        {
            logger.LogWarning("Role {RoleName} does not exist. Creating it now.", roleName);

            // Create the role
            var role = new ApplicationRole(roleName, $"Auto-created {roleName} role");
            var createResult = await roleManager.CreateAsync(role);

            if (!createResult.Succeeded)
            {
                var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                logger.LogError("Failed to create role {RoleName}: {Errors}", roleName, errors);
                throw new BadRequestException($"Failed to create role {roleName}: {errors}");
            }

            logger.LogInformation("Role {RoleName} created successfully", roleName);
        }

        // Assign role to user
        var assignResult = await userManager.AddToRoleAsync(user, roleName);

        if (!assignResult.Succeeded)
        {
            var errors = string.Join(", ", assignResult.Errors.Select(e => e.Description));
            logger.LogError("Failed to assign role {RoleName} to user {UserId}: {Errors}",
                roleName, user.Id, errors);
            throw new BadRequestException($"Failed to assign role: {errors}");
        }

        logger.LogInformation("User {UserId} assigned to role {RoleName}", user.Id, roleName);
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
}
