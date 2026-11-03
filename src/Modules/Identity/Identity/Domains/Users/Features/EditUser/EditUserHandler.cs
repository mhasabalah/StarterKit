using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Contracts.Users.Commands;
using Identity.Contracts.Users.Dtos;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Services;
using Identity.Services.Sessions;
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
    IRoleHierarchyGuard roleHierarchyGuard,
    IUserContactService contactService,
    ISessionRevoker sessionRevoker,
    IIdentityAuditWriter audit,
    ILogger<EditUserHandler> logger)
    : ICommandHandler<EditUserCommand, UserDto>
{
    public async Task<UserDto> Handle(EditUserCommand command, CancellationToken cancellationToken)
    {
        var callerId = httpContextService.GetUserIdAsGuid()
                       ?? throw new UnauthorizedException("Not authenticated");

        var user = await userManager.FindByIdAsync(command.UserId.ToString())
                   ?? throw new UserNotFoundException(command.UserId);

        await roleHierarchyGuard.EnsureCanManageAsync(callerId, user, cancellationToken);

        var before = UserAuditSnapshot.Of(user, await userManager.GetRolesAsync(user));

        var contactChange = await contactService.ApplyAsync(user, command.Email, command.PhoneNumber, cancellationToken);

        user.FirstName = command.FirstName;
        user.LastName = command.LastName;
        user.UpdatedAt = DateTime.UtcNow;

        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new InvalidUserOperationException($"Failed to update user: {errors}");
        }

        if (contactChange.Any)
        {
            // New, unverified contact details: whoever is signed in must prove ownership again.
            await sessionRevoker.RevokeAllAsync(user.Id, "Contact details changed by admin", cancellationToken);
        }

        logger.LogInformation("User {UserId} updated by admin {CallerId}", user.Id, callerId);

        var roles = await userManager.GetRolesAsync(user);

        audit.Record(IdentityAuditActions.UserEdited, user.Id,
            before: before, after: UserAuditSnapshot.Of(user, roles), actorId: callerId);

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
