using System.Text.RegularExpressions;
using Identity.Contracts.Auth.Commands;
using Identity.Domains.Auth.Mappings;
using Identity.Resources;
using Identity.Services.EmailConfirmation;
using Identity.Services.Phone;
using Identity.Services.Sessions;
using Microsoft.Extensions.Localization;
using Shared.Core.Phone;
using Shared.Core.Web;

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

    private bool BeValidIdentifier(string identifier) =>
        Regex.IsMatch(identifier, @"^[^@\s]+@[^@\s]+\.[^@\s]+$") || _phoneService.Validate(identifier, "ZZ").IsValid;
}

public class RegisterHandler(
    UserManager<ApplicationUser> userManager,
    RoleManager<ApplicationRole> roleManager,
    IAuthSessionService sessionService,
    IPhoneNumberNormalizer phoneNormalizer,
    IHttpContextService httpContextService,
    IEmailConfirmationSender emailConfirmationSender,
    IStringLocalizer<IdentityResource> localizer,
    ILogger<RegisterHandler> logger)
    : ICommandHandler<RegisterCommand, RegisterResult>
{
    public async Task<RegisterResult> Handle(RegisterCommand command, CancellationToken cancellationToken)
    {
        var user = DetectIdentifierType(command.Identifier) switch
        {
            IdentifierType.Email => await CreateEmailUserAsync(command),
            IdentifierType.Phone => await CreatePhoneUserAsync(command, cancellationToken),
            _ => throw new BadRequestException(localizer["Auth.InvalidIdentifier"])
        };

        await EnsureRoleExistsAndAssignAsync(user, RoleConstants.User);

        // Best effort (never fails registration); no-op for phone accounts (placeholder email).
        await emailConfirmationSender.TrySendAsync(user, cancellationToken);

        var session = await sessionService.IssueAsync(user, httpContextService.GetIpAddress(), cancellationToken);

        logger.LogInformation("User {UserId} registered", user.Id);

        return new RegisterResult(
            session.AccessToken,
            session.RefreshToken,
            session.AccessTokenExpiresAt,
            AuthUserDtoFactory.Create(user, session.Roles),
            localizer["Auth.Register.Success"]);
    }

    private IdentifierType DetectIdentifierType(string identifier)
    {
        if (identifier.Contains('@'))
        {
            return IdentifierType.Email;
        }

        if (identifier.StartsWith('+') || Regex.IsMatch(identifier, @"^\d{10,}$"))
        {
            return IdentifierType.Phone;
        }

        throw new BadRequestException(localizer["Auth.InvalidIdentifier"]);
    }

    private async Task<ApplicationUser> CreateEmailUserAsync(RegisterCommand command)
    {
        var email = command.Identifier.Trim();
        if (await userManager.FindByEmailAsync(email) is not null)
        {
            throw new BadRequestException(localizer["Auth.Register.EmailExists"]);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = email,
            Email = email,
            FirstName = command.FirstName,
            LastName = command.LastName,
            CreatedAt = DateTime.UtcNow
        };

        await CreateAsync(user, command.Password);
        return user;
    }

    private async Task<ApplicationUser> CreatePhoneUserAsync(RegisterCommand command, CancellationToken ct)
    {
        var phone = phoneNormalizer.Normalize(command.Identifier)
                    ?? throw new BadRequestException(localizer["Auth.InvalidIdentifier"]);

        if (await userManager.FindByPhoneAsync(phone, ct) is not null)
        {
            throw new BadRequestException(localizer["Auth.Register.PhoneExists"]);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = phone,
            // Identity requires a unique email; phone-only accounts get a non-routable placeholder.
            Email = $"{phone.TrimStart('+')}{ApplicationUser.PhonePlaceholderEmailDomain}",
            PhoneNumber = phone,
            FirstName = command.FirstName,
            LastName = command.LastName,
            CreatedAt = DateTime.UtcNow
        };

        await CreateAsync(user, command.Password);
        return user;
    }

    private async Task CreateAsync(ApplicationUser user, string password)
    {
        var result = await userManager.CreateAsync(user, password);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            throw new BadRequestException(localizer["Auth.Register.Failed", errors]);
        }
    }

    private async Task EnsureRoleExistsAndAssignAsync(ApplicationUser user, string roleName)
    {
        if (!await roleManager.RoleExistsAsync(roleName))
        {
            logger.LogWarning("Role {RoleName} does not exist. Creating it now.", roleName);
            var createResult = await roleManager.CreateAsync(new ApplicationRole(roleName, $"Auto-created {roleName} role"));
            if (!createResult.Succeeded)
            {
                var errors = string.Join(", ", createResult.Errors.Select(e => e.Description));
                throw new BadRequestException($"Failed to create role {roleName}: {errors}");
            }
        }

        var assignResult = await userManager.AddToRoleAsync(user, roleName);
        if (!assignResult.Succeeded)
        {
            var errors = string.Join(", ", assignResult.Errors.Select(e => e.Description));
            throw new BadRequestException($"Failed to assign role: {errors}");
        }
    }
}
