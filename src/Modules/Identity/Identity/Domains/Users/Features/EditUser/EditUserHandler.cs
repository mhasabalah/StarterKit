using Identity.Contracts.Users.Commands;
using Identity.Contracts.Users.Dtos;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;
using Shared.Core.Phone;
using Shared.Core.Web;

namespace Identity.Domains.Users.Features.EditUser;

public class EditUserCommandValidator : AbstractValidator<EditUserCommand>
{
    public EditUserCommandValidator(IPhoneValidationService phoneService)
    {
        RuleFor(x => x.UserId)
            .NotEmpty().WithMessage("User ID is required");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required")
            .EmailAddress().WithMessage("A valid email address is required");

        RuleFor(x => x.PhoneNumber)
            .ValidPhoneFor("ZZ", phoneService)
            .When(x => x.PhoneNumber is not null);
    }
}

public class EditUserHandler(
    UserManager<ApplicationUser> userManager,
    IHttpContextService httpContextService,
    IPhoneValidationService phoneService,
    ILogger<EditUserHandler> logger)
    : ICommandHandler<EditUserCommand, UserDto>
{
    public async Task<UserDto> Handle(EditUserCommand command, CancellationToken cancellationToken)
    {
        var callerId = httpContextService.GetUserIdAsGuid();

        logger.LogInformation("Admin {CallerId} editing user {UserId}", callerId, command.UserId);

        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null)
        {
            logger.LogWarning("User not found: {UserId}", command.UserId);
            throw new UserNotFoundException(command.UserId);
        }

        if (!string.Equals(user.Email, command.Email, StringComparison.OrdinalIgnoreCase))
        {
            var existingUser = await userManager.FindByEmailAsync(command.Email);
            if (existingUser is not null && existingUser.Id != user.Id)
                throw new InvalidOperationException($"Email '{command.Email}' is already in use.");

            await userManager.SetEmailAsync(user, command.Email);
        }

        // Normalize to E.164 before persisting
        var normalizedPhone = command.PhoneNumber is not null
            ? phoneService.ToE164(command.PhoneNumber, "ZZ") ?? command.PhoneNumber
            : null;

        var currentPhone = user.PhoneNumber ?? string.Empty;
        var newPhone = normalizedPhone ?? string.Empty;
        if (!string.Equals(currentPhone, newPhone, StringComparison.Ordinal))
            await userManager.SetPhoneNumberAsync(user, normalizedPhone);

        user.FirstName = command.FirstName;
        user.LastName = command.LastName;
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            logger.LogError("Failed to update user {UserId}: {Errors}", command.UserId, errors);
            throw new InvalidOperationException($"Failed to update user: {errors}");
        }

        logger.LogInformation("User {UserId} updated successfully by admin {CallerId}", command.UserId, callerId);

        var roles = await userManager.GetRolesAsync(user);
        var isLocked = await userManager.IsLockedOutAsync(user);
        var lockoutEnd = await userManager.GetLockoutEndDateAsync(user);

        return new UserDto
        {
            Id = user.Id,
            UserName = user.UserName!,
            Email = user.Email!,
            PhoneNumber = user.PhoneNumber,
            FirstName = user.FirstName,
            LastName = user.LastName,
            FullName = user.FullName,
            ProfilePictureUrl = user.ProfilePictureUrl,
            OAuthProvider = user.OAuthProvider,
            IsActive = user.IsActive,
            IsLocked = isLocked,
            LockoutEnd = lockoutEnd,
            EmailConfirmed = user.EmailConfirmed,
            PhoneNumberConfirmed = user.PhoneNumberConfirmed,
            TwoFactorEnabled = user.TwoFactorEnabled,
            AccessFailedCount = user.AccessFailedCount,
            IsProfileComplete = user.IsProfileComplete,
            ProfileCompletedAt = user.ProfileCompletedAt,
            Roles = roles.ToList(),
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt,
            LastLoginAt = user.LastLoginAt
        };
    }
}
