using Identity.Contracts.Onboarding.Commands;
using Identity.Data.Repository.Onboarding;
using Identity.Domains.Onboarding.Models;
using Shared.Core.Phone;

namespace Identity.Domains.Onboarding.Features.Register;

public class RegisterOnboardingCommandValidator : AbstractValidator<RegisterOnboardingCommand>
{
    public RegisterOnboardingCommandValidator(IPhoneValidationService phoneService)
    {
        RuleFor(x => x.PhoneNumber)
            .NotEmpty().WithMessage("Phone number is required")
            .ValidPhoneFor("ZZ", phoneService).WithMessage("Phone number must be in international format (+country_code number).");

        RuleFor(x => x.FirstName)
            .NotEmpty().WithMessage("First name is required");

        RuleFor(x => x.LastName)
            .NotEmpty().WithMessage("Last name is required");

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .Matches(@"[A-Z]").WithMessage("Password must contain uppercase letter")
            .Matches(@"[a-z]").WithMessage("Password must contain lowercase letter")
            .Matches(@"[0-9]").WithMessage("Password must contain digit")
            .Matches(@"[\W_]").WithMessage("Password must contain special character");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.Password).WithMessage("Passwords must match");

        RuleFor(x => x.Email)
            .EmailAddress()
            .When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Email must be a valid email address");
    }
}

public class RegisterHandler(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IOnboardingProcessRepository repository,
    ILogger<RegisterHandler> logger)
    : ICommandHandler<RegisterOnboardingCommand, RegisterOnboardingResult>
{
    public async Task<RegisterOnboardingResult> Handle(
        RegisterOnboardingCommand command,
        CancellationToken cancellationToken)
    {
        var existingUser = await userManager.Users
            .FirstOrDefaultAsync(u => u.PhoneNumber == command.PhoneNumber, cancellationToken);

        if (existingUser != null)
        {
            if (existingUser.IsPhoneNumberVerified)
            {
                throw new ConflictException("An account with this phone number is already verified. Please sign in.");
            }

            var completedProcess = await repository.GetByUserIdAndStatusAsync(
                existingUser.Id,
                OnboardingStatus.Complete,
                cancellationToken);
            if (completedProcess != null)
            {
                throw new ConflictException("An account with this phone number has already completed onboarding. Please sign in.");
            }

            var activeProcess = await repository.GetActiveByUserIdAsync(existingUser.Id, cancellationToken);
            if (activeProcess != null)
            {
                throw new ConflictException("Verification is already pending for this phone number.");
            }

            var reOnboardingProcess = OnboardingProcess.Create(existingUser.Id);
            await repository.AddAsync(reOnboardingProcess, cancellationToken);
            await repository.SaveChangesAsync(cancellationToken);

            logger.LogInformation(
                "Re-onboarding started for existing user {UserId} with phone {PhoneNumber}",
                existingUser.Id, command.PhoneNumber);

            return new RegisterOnboardingResult(
                existingUser.Id,
                "Registration successful. Please verify your phone number.",
                reOnboardingProcess.Status.ToString());
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = command.PhoneNumber,
            PhoneNumber = command.PhoneNumber,
            FirstName = command.FirstName,
            LastName = command.LastName,
            Email = string.IsNullOrWhiteSpace(command.Email) ? null : command.Email,
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

        await EnsureRoleExistsAndAssignAsync(user, RoleConstants.User);

        var process = OnboardingProcess.Create(user.Id);
        await repository.AddAsync(process, cancellationToken);
        await repository.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Onboarding registration started for new user {UserId} with phone {PhoneNumber}",
            user.Id, command.PhoneNumber);

        return new RegisterOnboardingResult(
            user.Id,
            "Registration successful. Please verify your phone number.",
            process.Status.ToString());
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
        }

        var assignResult = await userManager.AddToRoleAsync(user, roleName);

        if (!assignResult.Succeeded)
        {
            var errors = string.Join(", ", assignResult.Errors.Select(e => e.Description));
            logger.LogError("Failed to assign role {RoleName} to user {UserId}: {Errors}",
                roleName, user.Id, errors);
            throw new BadRequestException($"Failed to assign role: {errors}");
        }
    }
}
