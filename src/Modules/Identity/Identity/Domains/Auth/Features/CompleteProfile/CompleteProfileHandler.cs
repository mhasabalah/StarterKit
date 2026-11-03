using Identity.Contracts.Auth.Commands;
using Identity.Services.EmailConfirmation;
using Identity.Services.Phone;
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
    IPhoneNumberNormalizer phoneNormalizer,
    IEmailConfirmationSender emailConfirmationSender,
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

        // Add email only if the account has none yet (phone-registered users: null or the non-routable placeholder)
        var emailAdded = false;
        if (command.Email is not null && !HasRealEmail(user))
        {
            var existing = await userManager.FindByEmailAsync(command.Email);
            if (existing is not null)
                throw new ConflictException("Email already in use.");

            user.Email = command.Email;
            user.UserName = command.Email;
            user.EmailConfirmed = false;
            emailAdded = true;
        }

        // Add phone only if the account has none yet (email-registered users)
        var phoneNumber = phoneNormalizer.Normalize(command.PhoneNumber);
        if (phoneNumber is not null && string.IsNullOrEmpty(user.PhoneNumber))
        {
            var existing = await userManager.FindByPhoneAsync(phoneNumber, cancellationToken);
            if (existing is not null)
                throw new ConflictException("Phone number already in use.");

            user.PhoneNumber = phoneNumber;
            user.IsPhoneNumberVerified = false;
        }

        user.UpdatedAt = DateTime.UtcNow;
        user.UpdateProfileCompletion();

        var update = await userManager.UpdateAsync(user);
        if (!update.Succeeded)
        {
            throw new BadRequestException(string.Join(", ", update.Errors.Select(e => e.Description)));
        }

        logger.LogInformation("Profile updated for user {UserId}", userId);

        if (emailAdded)
        {
            await emailConfirmationSender.TrySendAsync(user, cancellationToken);
        }

        return new CompleteProfileResult(
            user.IsProfileComplete,
            RequiresPhoneVerification: !user.IsPhoneNumberVerified,
            RequiresEmailVerification: !user.EmailConfirmed,
            user.IsProfileComplete
                ? "Profile completed successfully."
                : "Profile updated. Please verify your phone number.");
    }

    private static bool HasRealEmail(ApplicationUser user) =>
        !string.IsNullOrEmpty(user.Email) &&
        !user.Email.EndsWith(ApplicationUser.PhonePlaceholderEmailDomain, StringComparison.OrdinalIgnoreCase);
}
