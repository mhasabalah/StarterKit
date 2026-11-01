using Identity.Contracts.Auth.Commands;
using Identity.Options;
using Microsoft.Extensions.Options;
using Shared.Core.Phone;
using Shared.Email.Options;
using Shared.Email.Contracts;
using Shared.Email.Enums;
using Shared.Email.Models;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace Identity.Domains.Auth.Features.ForgotPassword;

public class ForgotPasswordCommandValidator : AbstractValidator<ForgotPasswordCommand>
{
    public ForgotPasswordCommandValidator(IPhoneValidationService phoneService)
    {
        RuleFor(x => x.Identifier)
            .NotEmpty().WithMessage("Identifier is required")
            .Must(id => IsValidEmail(id) || phoneService.Validate(id, "ZZ").IsValid)
            .WithMessage("Must be a valid email address or international phone number (+country_code number)");
    }

    private static bool IsValidEmail(string value) =>
        Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");
}

public class ForgotPasswordHandler(
    UserManager<ApplicationUser> userManager,
    IEmailJobEnqueuer emailJobEnqueuer,
    ISmsService smsService,
    IOptions<PasswordResetOptions> resetOptions,
    IOptions<PhoneVerificationOptions> phoneOptions,
    IOptions<EmailOptions> emailOptions,
    ILogger<ForgotPasswordHandler> logger)
    : ICommandHandler<ForgotPasswordCommand, ForgotPasswordResult>
{
    private readonly PasswordResetOptions _resetOptions = resetOptions.Value;
    private readonly PhoneVerificationOptions _phoneOptions = phoneOptions.Value;

    private const string GenericMessage =
        "If an account with that identifier exists, reset instructions have been sent.";

    public async Task<ForgotPasswordResult> Handle(
        ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        if (command.Identifier.Contains('@'))
            return await HandleEmailReset(command.Identifier, cancellationToken);

        return await HandlePhoneReset(command.Identifier, cancellationToken);
    }

    private async Task<ForgotPasswordResult> HandleEmailReset(
        string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);

        // Reject missing users or phone-only placeholder accounts silently
        if (user is null || user.Email!.EndsWith(ApplicationUser.PhonePlaceholderEmailDomain))
        {
            logger.LogWarning("Password reset requested for unknown or phone-only email");
            return new ForgotPasswordResult(GenericMessage);
        }

        // OAuth accounts have no password — return generic response, do not hint
        if (user.OAuthProvider is not null)
        {
            logger.LogInformation("Password reset silently blocked for OAuth user {UserId}", user.Id);
            return new ForgotPasswordResult(GenericMessage);
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetUrl = $"{_resetOptions.FrontendBaseUrl}/reset-password" +
                       $"?userId={user.Id}&token={Uri.EscapeDataString(token)}";

        var model = new PasswordResetRequiredModel
        {
            CompanyName = emailOptions.Value.BrandName,
            ResetUrl = resetUrl,
            ExpiresInHours = _resetOptions.TokenExpiryHours
        };

        var message = new EmailMessage(
            Template: EmailTemplate.PasswordResetRequired,
            Model: model.ToDictionary(),
            Recipients: [user.Email!],
            Culture: EmailCulture.En,
            UserId: user.Id
        );

        await emailJobEnqueuer.EnqueueAsync(message, cancellationToken);

        logger.LogInformation("Password reset email enqueued for user {UserId}", user.Id);

        return new ForgotPasswordResult(GenericMessage);
    }

    private async Task<ForgotPasswordResult> HandlePhoneReset(
        string phone, CancellationToken cancellationToken)
    {
        var user = await userManager.Users
            .FirstOrDefaultAsync(u => u.PhoneNumber == phone, cancellationToken);

        if (user is null)
        {
            logger.LogWarning("Password reset requested for unknown phone");
            return new ForgotPasswordResult(GenericMessage);
        }

        if (user.OAuthProvider is not null)
            return new ForgotPasswordResult(GenericMessage);

        if (!user.IsPhoneNumberVerified)
            return new ForgotPasswordResult(GenericMessage);

        // Generate OTP using same length config as phone verification
        var min = (int)Math.Pow(10, _phoneOptions.CodeLength - 1);
        var max = (int)Math.Pow(10, _phoneOptions.CodeLength) - 1;
        var otp = RandomNumberGenerator.GetInt32(min, max + 1).ToString();
        var expiry = DateTime.UtcNow.AddMinutes(_resetOptions.OtpExpiryMinutes);

        user.PhoneVerificationCode = otp;
        user.PhoneVerificationCodeExpiry = expiry;
        user.UpdatedAt = DateTime.UtcNow;

        var updateResult = await userManager.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            logger.LogError(
                "Failed to persist OTP for user {UserId}: {Errors}",
                user.Id,
                string.Join(", ", updateResult.Errors.Select(e => e.Description)));
            return new ForgotPasswordResult(GenericMessage);
        }

        await smsService.SendVerificationCodeAsync(phone, otp);

        logger.LogInformation("Password reset OTP sent for user {UserId}", user.Id);

        return new ForgotPasswordResult(GenericMessage);
    }
}
