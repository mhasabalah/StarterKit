using Identity.Contracts.Auth.Commands;

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

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Password is required")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters")
            .Matches(@"[A-Z]").WithMessage("Password must contain an uppercase letter")
            .Matches(@"[a-z]").WithMessage("Password must contain a lowercase letter")
            .Matches(@"[0-9]").WithMessage("Password must contain a digit")
            .Matches(@"[\W_]").WithMessage("Password must contain a special character");

        RuleFor(x => x.ConfirmPassword)
            .Equal(x => x.NewPassword).WithMessage("Passwords do not match");
    }
}

public class ResetPasswordHandler(
    UserManager<ApplicationUser> userManager,
    ILogger<ResetPasswordHandler> logger)
    : ICommandHandler<ResetPasswordCommand, ResetPasswordResult>
{
    public async Task<ResetPasswordResult> Handle(
        ResetPasswordCommand command, CancellationToken cancellationToken)
    {
        var user = await FindUserAsync(command);

        if (user is null)
        {
            // Do not reveal whether the userId is valid
            throw new BadRequestException("Invalid or expired reset token.");
        }

        var result = await userManager.ResetPasswordAsync(user, command.Token, command.NewPassword);

        if (!result.Succeeded)
        {
            logger.LogWarning("Password reset failed for user {UserId}", user.Id);
            // Return generic message — do not expose Identity error details to client
            throw new BadRequestException("Invalid or expired reset token.");
        }

        if (user.MustChangePasswordOnNextLogin)
        {
            user.MustChangePasswordOnNextLogin = false;
            await userManager.UpdateAsync(user);
        }

        logger.LogInformation("Password reset completed for user {UserId}", user.Id);

        return new ResetPasswordResult(
            "Password reset successfully. You can now log in with your new password.");
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
