using Identity.Contracts.Auth.Commands;
using Identity.Contracts.Auth.Dtos;
using Identity.Services.Token;
using Identity.Domains.Roles.Constants;
using Shared.Core.Phone;

namespace Identity.Domains.Auth.Features.RegisterWithPhone;

public class RegisterWithPhoneCommandValidator : AbstractValidator<RegisterWithPhoneCommand>
{
    public RegisterWithPhoneCommandValidator(IPhoneValidationService phoneService)
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required")
            .ValidPhoneFor("ZZ", phoneService).WithMessage("Phone number must be in international format (+country_code number).");
        
        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .Matches(@"[A-Z]").WithMessage("Password must contain uppercase letter")
            .Matches(@"[a-z]").WithMessage("Password must contain lowercase letter")
            .Matches(@"[0-9]").WithMessage("Password must contain digit")
            .Matches(@"[\W_]").WithMessage("Password must contain special character");
        
        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password).WithMessage("Passwords must match");
    }
}

public class RegisterWithPhoneHandler(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IJwtTokenGenerator tokenGenerator,
    ILogger<RegisterWithPhoneHandler> logger) : ICommandHandler<RegisterWithPhoneCommand, RegisterResult>
{
    public async Task<RegisterResult> Handle(RegisterWithPhoneCommand command, CancellationToken cancellationToken)
    {
        var existingUser = await userManager.Users
            .FirstOrDefaultAsync(u => u.PhoneNumber == command.PhoneNumber, cancellationToken);
        
        if (existingUser != null)
        {
            throw new BadRequestException("User with this phone number already exists");
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = command.PhoneNumber,
            PhoneNumber = command.PhoneNumber,
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
            throw new BadRequestException($"Registration failed: {errors}");
        }

        // Assign default User role
        await EnsureRoleExistsAndAssignAsync(user, RoleConstants.User);

        // Generate tokens
        var accessToken = await tokenGenerator.GenerateTokenAsync(user);
        var refreshToken = tokenGenerator.GenerateRefreshToken();
        var expiresAt = DateTime.UtcNow.AddHours(1);

        // Get roles for user DTO
        var roles = await userManager.GetRolesAsync(user);
        var userDto = CreateUserDto(user, roles);

        logger.LogInformation("User with phone {PhoneNumber} registered successfully", command.PhoneNumber);

        return new RegisterResult(
            accessToken,
            refreshToken,
            expiresAt,
            userDto,
            "Registration successful. Welcome to StarterKit!"
        );
    }

    private async Task EnsureRoleExistsAndAssignAsync(ApplicationUser user, string roleName)
    {
        var roleExists = await roleManager.RoleExistsAsync(roleName);

        if (!roleExists)
        {
            logger.LogWarning("Role {RoleName} does not exist. Creating it now.", roleName);

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
