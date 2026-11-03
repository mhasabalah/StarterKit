using Identity.Contracts.Auth.Commands;

namespace Identity.Domains.Auth.Features.ConfirmEmail;

public class ConfirmEmailCommandValidator : AbstractValidator<ConfirmEmailCommand>
{
    public ConfirmEmailCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty().WithMessage("User ID is required");
        RuleFor(x => x.Token).NotEmpty().WithMessage("Confirmation token is required").MaximumLength(2048);
    }
}

/// <summary>
/// Confirms the email the token was issued for. An unknown user, a wrong or expired token, and a token issued before an
/// email or password change all return the same error, so the endpoint can't be used to probe accounts.
/// </summary>
public class ConfirmEmailHandler(
    UserManager<ApplicationUser> userManager,
    ILogger<ConfirmEmailHandler> logger)
    : ICommandHandler<ConfirmEmailCommand, ConfirmEmailResult>
{
    public const string InvalidTokenMessage = "Invalid or expired confirmation link.";
    public const string ConfirmedMessage = "Your email has been confirmed.";

    public async Task<ConfirmEmailResult> Handle(ConfirmEmailCommand command, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByIdAsync(command.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            throw new BadRequestException(InvalidTokenMessage);
        }

        var result = await userManager.ConfirmEmailAsync(user, command.Token);
        if (!result.Succeeded)
        {
            logger.LogWarning("Email confirmation failed for user {UserId}", user.Id);
            throw new BadRequestException(InvalidTokenMessage);
        }

        logger.LogInformation("Email confirmed for user {UserId}", user.Id);
        return new ConfirmEmailResult(ConfirmedMessage);
    }
}
