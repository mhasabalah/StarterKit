using Identity.Contracts.Auth.Commands;
using Identity.Domains.Auth.Validation;
using Identity.Services.Sessions;

namespace Identity.Domains.Auth.Features.ResetPassword;

public class ResetPasswordCommandValidator : AbstractValidator<ResetPasswordCommand>
{
    public ResetPasswordCommandValidator()
    {
        RuleFor(x => x)
            .Must(x => x.UserId is { } userId && userId != Guid.Empty || !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("User ID or email is required");

        RuleFor(x => x.Email)
            .EmailAddress().WithMessage("A valid email is required")
            .MaximumLength(256)
            .When(x => !string.IsNullOrWhiteSpace(x.Email));

        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Reset token is required");

        RuleFor(x => x.NewPassword).ApplyPasswordRules();

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword).WithMessage("Passwords do not match");
    }
}

public class ResetPasswordHandler(
    UserManager<ApplicationUser> userManager,
    ISessionRevoker sessionRevoker,
    ILogger<ResetPasswordHandler> logger)
    : ICommandHandler<ResetPasswordCommand, ResetPasswordResult>
{
    public const string InvalidTokenMessage = "Invalid or expired reset token.";

    public async Task<ResetPasswordResult> Handle(ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(command)
                   ?? throw new BadRequestException(InvalidTokenMessage);

        var result = await userManager.ResetPasswordAsync(user, command.Token, command.NewPassword);
        if (!result.Succeeded)
        {
            logger.LogWarning("Password reset failed for user {UserId}", user.Id);
            throw new BadRequestException(InvalidTokenMessage);
        }

        if (user.MustChangePasswordOnNextLogin)
        {
            user.MustChangePasswordOnNextLogin = false;
            await userManager.UpdateAsync(user);
        }

        // Whoever had the old password (or a stolen session) is signed out everywhere.
        await sessionRevoker.RevokeAllAsync(user.Id, "Password reset", cancellationToken);

        logger.LogInformation("Password reset completed for user {UserId}", user.Id);

        return new ResetPasswordResult("Password reset successfully. You can now log in with your new password.");
    }

    private async Task<ApplicationUser?> FindUserAsync(ResetPasswordCommand command)
    {
        if (command.UserId is { } userId && userId != Guid.Empty)
        {
            return await userManager.FindByIdAsync(userId.ToString());
        }

        return string.IsNullOrWhiteSpace(command.Email)
            ? null
            : await userManager.FindByEmailAsync(command.Email.Trim());
    }
}
