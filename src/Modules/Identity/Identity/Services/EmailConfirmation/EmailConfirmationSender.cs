using Identity.Options;
using Microsoft.Extensions.Options;
using Notification.Contracts.Email.Models;

namespace Identity.Services.EmailConfirmation;

public interface IEmailConfirmationSender
{
    /// <summary>
    /// Sends a confirmation link to the user's current email. Returns <c>false</c> (nothing sent) when the account
    /// has no real email, is already confirmed, or is inactive.
    /// </summary>
    Task<bool> SendAsync(ApplicationUser user, CancellationToken cancellationToken = default);

    /// <summary>Same as <see cref="SendAsync"/>, but never throws: used right after register/profile updates.</summary>
    Task TrySendAsync(ApplicationUser user, CancellationToken cancellationToken = default);
}

public sealed class EmailConfirmationSender(
    UserManager<ApplicationUser> userManager,
    INotificationSender notificationSender,
    IOptions<EmailConfirmationOptions> options,
    ILogger<EmailConfirmationSender> logger) : IEmailConfirmationSender
{
    private readonly EmailConfirmationOptions _options = options.Value;

    public static bool CanConfirm(ApplicationUser user) =>
        user.IsActive &&
        !user.EmailConfirmed &&
        !string.IsNullOrWhiteSpace(user.Email) &&
        !user.Email.EndsWith(ApplicationUser.PhonePlaceholderEmailDomain, StringComparison.OrdinalIgnoreCase);

    public async Task<bool> SendAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        if (!CanConfirm(user))
        {
            return false;
        }

        // The token is bound to the user's security stamp: changing email or password invalidates older links.
        var token = await userManager.GenerateEmailConfirmationTokenAsync(user);
        var confirmUrl = $"{_options.FrontendBaseUrl.TrimEnd('/')}/confirm-email" +
                         $"?userId={user.Id}&token={Uri.EscapeDataString(token)}";

        var model = new EmailConfirmationModel
        {
            FirstName = user.FirstName ?? string.Empty,
            ConfirmUrl = confirmUrl,
            ExpiresInHours = Math.Max(1, _options.TokenExpiryHours)
        };

        // To the address the token was issued for, forced to Email so channel preferences can't suppress it.
        await notificationSender
            .ToEmail(user.Email!, NotificationCulture.En, user.Id)
            .WithEmail(EmailTemplate.EmailConfirmation, model.ToDictionary())
            .SendAsync(NotificationChannel.Email, cancellationToken);

        logger.LogInformation("Email confirmation enqueued for user {UserId}", user.Id);
        return true;
    }

    public async Task TrySendAsync(ApplicationUser user, CancellationToken cancellationToken = default)
    {
        if (!_options.SendOnRegister)
        {
            return;
        }

        try
        {
            await SendAsync(user, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Registration must not fail because mail is down; the user can resend from the app.
            logger.LogError(ex, "Failed to enqueue email confirmation for user {UserId}", user.Id);
        }
    }
}
