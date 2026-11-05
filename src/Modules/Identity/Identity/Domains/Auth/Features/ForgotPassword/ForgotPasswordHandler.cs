using System.Text.RegularExpressions;
using Identity.Contracts.Auth.Commands;
using Identity.Options;
using Identity.Services.Otp;
using Identity.Services.Phone;
using Microsoft.Extensions.Options;
using Shared.Core.Phone;
using Notification.Contracts.Email.Models;

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

/// <summary>
/// Sends reset instructions ONLY to a contact the account owner has proven: a confirmed email or a verified phone.
/// (An admin-changed, unverified contact must never receive a reset.) The response is identical in every case.
/// </summary>
public class ForgotPasswordHandler(
    UserManager<ApplicationUser> userManager,
    INotificationSender notificationSender,
    ISmsService smsService,
    IOtpCodeService otpCodeService,
    IPhoneNumberNormalizer phoneNormalizer,
    IOptions<PasswordResetOptions> resetOptions,
    Identity.Services.Sso.ISsoEnforcementPolicy ssoPolicy,
    ILogger<ForgotPasswordHandler> logger)
    : ICommandHandler<ForgotPasswordCommand, ForgotPasswordResult>
{
    public const string GenericMessage =
        "If an account with that identifier exists, reset instructions have been sent.";

    private readonly PasswordResetOptions _resetOptions = resetOptions.Value;

    public async Task<ForgotPasswordResult> Handle(ForgotPasswordCommand command, CancellationToken cancellationToken)
    {
        if (command.Identifier.Contains('@'))
        {
            await HandleEmailResetAsync(command.Identifier.Trim(), cancellationToken);
        }
        else
        {
            await HandlePhoneResetAsync(command.Identifier, cancellationToken);
        }

        return new ForgotPasswordResult(GenericMessage);
    }

    private async Task HandleEmailResetAsync(string email, CancellationToken cancellationToken)
    {
        var user = await userManager.FindByEmailAsync(email);

        if (user is null || !CanReset(user) || !user.EmailConfirmed ||
            user.Email!.EndsWith(ApplicationUser.PhonePlaceholderEmailDomain, StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInformation("Password reset email not sent (unknown, unconfirmed or ineligible account)");
            return;
        }

        // The company IdP owns the credential: a local password would bypass SSO enforcement. Same generic response.
        if (!await ssoPolicy.IsPasswordResetAllowedAsync(user, cancellationToken))
        {
            logger.LogInformation("Password reset email not sent for user {UserId}: SSO is enforced", user.Id);
            return;
        }

        var token = await userManager.GeneratePasswordResetTokenAsync(user);
        var resetUrl = $"{_resetOptions.FrontendBaseUrl}/reset-password" +
                       $"?userId={user.Id}&token={Uri.EscapeDataString(token)}";

        var model = new PasswordResetRequiredModel
        {
            ResetUrl = resetUrl,
            ExpiresInHours = _resetOptions.TokenExpiryHours
        };

        // To the CONFIRMED address checked above (not whatever the directory says at delivery time), forced to Email
        // so channel preferences can never suppress a security mail. userId only links the delivery log.
        await notificationSender
            .ToEmail(user.Email!, NotificationCulture.En, user.Id)
            .WithEmail(EmailTemplate.PasswordResetRequired, model.ToDictionary())
            .SendAsync(NotificationChannel.Email, cancellationToken);

        logger.LogInformation("Password reset email enqueued for user {UserId}", user.Id);
    }

    private async Task HandlePhoneResetAsync(string rawPhone, CancellationToken cancellationToken)
    {
        var phone = phoneNormalizer.Normalize(rawPhone);
        var user = phone is null ? null : await userManager.FindByPhoneAsync(phone, cancellationToken);

        if (user is null || !CanReset(user) || !user.IsPhoneNumberVerified)
        {
            logger.LogInformation("Password reset SMS not sent (unknown, unverified or ineligible account)");
            return;
        }

        try
        {
            var otp = await otpCodeService.IssueAsync(user.Id, OtpPurpose.PasswordReset, cancellationToken);
            await smsService.SendVerificationCodeAsync(user.PhoneNumber!, otp.Code);
            logger.LogInformation("Password reset code sent to user {UserId}", user.Id);
        }
        catch (OtpRateLimitedException)
        {
            // Same generic response: a 429 here would reveal that the phone number has an account.
            logger.LogWarning("Password reset code for user {UserId} rate limited", user.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Failed to send password reset code to user {UserId}", user.Id);
        }
    }

    /// <summary>OAuth-only and deactivated accounts have no password to reset.</summary>
    private static bool CanReset(ApplicationUser user) => user.OAuthProvider is null && user.IsActive;
}
