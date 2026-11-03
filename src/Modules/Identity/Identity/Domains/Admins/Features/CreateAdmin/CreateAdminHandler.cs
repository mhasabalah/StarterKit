using Identity.Domains.Audit.Models;
using Identity.Domains.Audit.Services;
using Identity.Contracts.Admins.Commands;
using Identity.Domains.Admins.Events;
using Identity.Domains.Auth.Validation;
using Identity.Domains.Users.Services;
using Identity.Services.Phone;
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

        RuleFor(x => x.Password).ApplyPasswordRules();

        RuleFor(x => x.CreatedByAdminId)
            .NotEmpty()
            .WithMessage("Creator admin ID is required");

        RuleFor(x => x.PhoneNumber)
            .ValidPhoneFor("ZZ", phoneService)
            .When(x => x.PhoneNumber is not null);
    }
}

/// <summary>Creating Admin/SuperAdmin accounts is a SuperAdmin-only operation (same rule as PromoteToAdmin).</summary>
public class CreateAdminHandler(
    UserManager<ApplicationUser> userManager,
    IRoleHierarchyGuard roleHierarchyGuard,
    IPhoneNumberNormalizer phoneNormalizer,
    IPublisher publisher,
    IIdentityAuditWriter audit,
    ILogger<CreateAdminHandler> logger)
    : ICommandHandler<CreateAdminCommand, CreateAdminResult>
{
    public async Task<CreateAdminResult> Handle(CreateAdminCommand command, CancellationToken cancellationToken)
    {
        await roleHierarchyGuard.EnsureIsSuperAdminAsync(command.CreatedByAdminId, cancellationToken);

        if (await userManager.FindByNameAsync(command.UserName) is not null)
        {
            return Failure($"Username '{command.UserName}' is already taken");
        }

        if (await userManager.FindByEmailAsync(command.Email) is not null)
        {
            return Failure("Email is already registered");
        }

        var phoneNumber = phoneNormalizer.Normalize(command.PhoneNumber);
        if (phoneNumber is not null && await userManager.FindByPhoneAsync(phoneNumber, cancellationToken) is not null)
        {
            return Failure("Phone number is already registered");
        }

        var newAdmin = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = command.UserName,
            Email = command.Email,
            EmailConfirmed = true,
            FirstName = command.FirstName,
            LastName = command.LastName,
            PhoneNumber = phoneNumber,
            PhoneNumberConfirmed = phoneNumber is not null,
            IsActive = true,
            IsProfileComplete = true,
            ProfileCompletedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };

        var createResult = await userManager.CreateAsync(newAdmin, command.Password);
        if (!createResult.Succeeded)
        {
            var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
            logger.LogError("Failed to create admin: {Errors}", errors);
            return Failure($"Failed to create admin: {errors}");
        }

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

        // The password is never audited: the snapshot has no credential fields.
        audit.Record(IdentityAuditActions.AdminCreated, newAdmin.Id,
            after: UserAuditSnapshot.Of(newAdmin, await userManager.GetRolesAsync(newAdmin)),
            actorId: command.CreatedByAdminId);

        await publisher.Publish(new AdminCreatedEvent
        {
            AdminId = newAdmin.Id,
            UserName = newAdmin.UserName,
            IsSuperAdmin = command.IsSuperAdmin,
            CreatedBy = command.CreatedByAdminId
        }, cancellationToken);

        logger.LogInformation(
            "Admin {AdminId} created by {CreatedBy} with roles {Roles}",
            newAdmin.Id, command.CreatedByAdminId, string.Join(", ", rolesToAssign));

        return new CreateAdminResult(
            true,
            $"Admin account created successfully for {newAdmin.UserName}",
            newAdmin.Id,
            rolesToAssign.ToList());
    }

    private static CreateAdminResult Failure(string message) => new(false, message, null, Array.Empty<string>());
}
