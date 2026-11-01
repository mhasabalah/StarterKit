using Identity.Contracts.Admins.Commands;
using Identity.Domains.Admins.Events;
using Identity.Domains.Roles.Constants;
using Identity.Domains.Users.Exceptions;
using Identity.Domains.Users.Models;
using Microsoft.AspNetCore.Identity;
using Shared.Core.Phone;

namespace Identity.Domains.Admins.Features.CreateAdmin;

public class CreateAdminCommandValidator : AbstractValidator<CreateAdminCommand>
{
    public CreateAdminCommandValidator(IPhoneValidationService phoneService)
    {
        RuleFor(x => x.UserName)
            .NotEmpty()
            .MinimumLength(3)
            .MaximumLength(50)
            .WithMessage("Username must be between 3 and 50 characters");

        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress()
            .WithMessage("Valid email address is required");

        RuleFor(x => x.Password)
            .NotEmpty()
            .MinimumLength(8)
            .WithMessage("Password must be at least 8 characters");

        RuleFor(x => x.CreatedByAdminId)
            .NotEmpty()
            .WithMessage("Creator admin ID is required");

        RuleFor(x => x.PhoneNumber)
            .ValidPhoneFor("ZZ", phoneService)
            .When(x => x.PhoneNumber is not null);
    }
}

public class CreateAdminHandler(
    UserManager<ApplicationUser> userManager,
    IPublisher publisher,
    ILogger<CreateAdminHandler> logger)
    : ICommandHandler<CreateAdminCommand, CreateAdminResult>
{
    public async Task<CreateAdminResult> Handle(
        CreateAdminCommand command,
        CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Creating new admin: {UserName} by {CreatedBy}",
            command.UserName, command.CreatedByAdminId);

        // Verify creator exists and has sufficient privileges
        var creator = await userManager.FindByIdAsync(command.CreatedByAdminId.ToString())
            ?? throw new UserNotFoundException(command.CreatedByAdminId);

        var creatorRoles = await userManager.GetRolesAsync(creator);
        // Creating a SuperAdmin account requires the caller to be a SuperAdmin
        if (command.IsSuperAdmin && !creatorRoles.Contains(RoleConstants.SuperAdmin))
        {
            throw new InvalidUserOperationException(
                "Only SuperAdmin can create SuperAdmin accounts.");
        }

        // Check if username already exists
        var existingUser = await userManager.FindByNameAsync(command.UserName);
        if (existingUser != null)
        {
            logger.LogWarning("Username already exists: {UserName}", command.UserName);
            return new CreateAdminResult(
                false,
                $"Username '{command.UserName}' is already taken",
                null,
                Array.Empty<string>());
        }

        // Check if email already exists
        existingUser = await userManager.FindByEmailAsync(command.Email);
        if (existingUser != null)
        {
            logger.LogWarning("Email already exists: {Email}", command.Email);
            return new CreateAdminResult(
                false,
                $"Email '{command.Email}' is already registered",
                null,
                Array.Empty<string>());
        }

        // Create new admin user
        var newAdmin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = command.UserName,
            Email = command.Email,
            EmailConfirmed = true, // Auto-confirm for admin accounts
            FirstName = command.FirstName,
            LastName = command.LastName,
            PhoneNumber = command.PhoneNumber,
            PhoneNumberConfirmed = !string.IsNullOrEmpty(command.PhoneNumber),
            IsActive = true,
            IsProfileComplete = true,
            ProfileCompletedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        // Create user with password
        var createResult = await userManager.CreateAsync(newAdmin, command.Password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
            logger.LogError("Failed to create admin: {Errors}", errors);
            return new CreateAdminResult(false, $"Failed to create admin: {errors}", null, Array.Empty<string>());
        }

        // Assign roles
        var rolesToAssign = command.IsSuperAdmin
            ? new[] { RoleConstants.User, RoleConstants.Admin, RoleConstants.SuperAdmin }
            : new[] { RoleConstants.User, RoleConstants.Admin };

        foreach (var role in rolesToAssign)
        {
            var roleResult = await userManager.AddToRoleAsync(newAdmin, role);
            if (!roleResult.Succeeded)
            {
                logger.LogError("Failed to assign role {Role} to admin {AdminId}", role, newAdmin.Id);
            }
        }

        // Raise domain event
        await publisher.Publish(new AdminCreatedEvent
        {
            AdminId = newAdmin.Id,
            UserName = newAdmin.UserName,
            IsSuperAdmin = command.IsSuperAdmin,
            CreatedBy = command.CreatedByAdminId
        }, cancellationToken);

        logger.LogInformation(
            "Admin created successfully: {AdminId} with roles: {Roles}",
            newAdmin.Id, string.Join(", ", rolesToAssign));

        return new CreateAdminResult(
            true,
            $"Admin account created successfully for {newAdmin.UserName}",
            newAdmin.Id,
            rolesToAssign.ToList());
    }
}
