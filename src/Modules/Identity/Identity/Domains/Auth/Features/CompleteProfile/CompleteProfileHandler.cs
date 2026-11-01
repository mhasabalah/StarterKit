using Identity.Contracts.Auth.Commands;
using Shared.Core.Phone;
using Shared.Core.Web;

namespace Identity.Domains.Auth.Features.CompleteProfile;

public class CompleteProfileCommandValidator : AbstractValidator<CompleteProfileCommand>
{
    public CompleteProfileCommandValidator(IPhoneValidationService phoneService)
    {
        RuleFor(x => x.FirstName)
            .MaximumLength(100).When(x => x.FirstName is not null);
        RuleFor(x => x.LastName)
            .MaximumLength(100).When(x => x.LastName is not null);
        RuleFor(x => x.Email)
            .EmailAddress().When(x => x.Email is not null);
        RuleFor(x => x.PhoneNumber)
            .ValidPhoneFor("ZZ", phoneService)
            .When(x => x.PhoneNumber is not null);
        RuleFor(x => x.ProfilePictureUrl)
            .MaximumLength(2000).When(x => x.ProfilePictureUrl is not null);
    }
}

public class CompleteProfileHandler(
    UserManager<ApplicationUser> userManager,
    IHttpContextService httpContextService,
    ILogger<CompleteProfileHandler> logger)
    : ICommandHandler<CompleteProfileCommand, CompleteProfileResult>
{
    public async Task<CompleteProfileResult> Handle(
        CompleteProfileCommand command,
        CancellationToken cancellationToken)
    {
        var userId = httpContextService.GetUserIdAsGuid()
            ?? throw new ForbiddenException("User identity not found.");

        var user = await userManager.FindByIdAsync(userId.ToString())
            ?? throw new NotFoundException("User not found.");

        if (command.FirstName is not null)
            user.FirstName = command.FirstName;

        if (command.LastName is not null)
            user.LastName = command.LastName;

        if (command.ProfilePictureUrl is not null)
            user.ProfilePictureUrl = command.ProfilePictureUrl;

        // Add email only if the account has none yet (phone-registered users)
        if (command.Email is not null && string.IsNullOrEmpty(user.Email))
        {
            var existing = await userManager.FindByEmailAsync(command.Email);
            if (existing is not null)
                throw new ConflictException("Email already in use.");

            user.Email = command.Email;
            user.UserName = command.Email;
            user.EmailConfirmed = false;
        }

        // Add phone only if the account has none yet (email-registered users)
        if (command.PhoneNumber is not null && string.IsNullOrEmpty(user.PhoneNumber))
        {
            var existing = await userManager.Users
                .FirstOrDefaultAsync(u => u.PhoneNumber == command.PhoneNumber, cancellationToken);
            if (existing is not null)
                throw new ConflictException("Phone number already in use.");

            user.PhoneNumber = command.PhoneNumber;
            user.IsPhoneNumberVerified = false;
        }

        user.UpdatedAt = DateTime.UtcNow;
        user.UpdateProfileCompletion();

        await userManager.UpdateAsync(user);

        logger.LogInformation("Profile updated for user {UserId}", userId);

        return new CompleteProfileResult(
            user.IsProfileComplete,
            RequiresPhoneVerification: !user.IsPhoneNumberVerified,
            RequiresEmailVerification: !user.EmailConfirmed,
            user.IsProfileComplete
                ? "Profile completed successfully."
                : "Profile updated. Please verify your phone number.");
    }
}
