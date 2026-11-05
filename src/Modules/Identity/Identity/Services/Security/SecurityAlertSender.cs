using System.Globalization;
using Identity.Options;
using Microsoft.Extensions.Options;
using Notification.Contracts.Email.Models;
using Shared.Core.Web;

namespace Identity.Services.Security;

/// <summary>
/// "Your sign-in methods changed" emails (passkey added/removed, SSO identity linked). Sent only to a CONFIRMED,
/// real address and forced to the Email channel, like every security mail. Best effort: a failure to enqueue is
/// logged and never fails the change itself (the change is already audited). The mail uses the culture of the request
/// that made the change (the user's own action), so an Arabic session gets an Arabic alert.
/// </summary>
public interface ISecurityAlertSender
{
    Task SendAsync(ApplicationUser user, string eventKey, string? detail, CancellationToken ct = default);
}

public sealed class SecurityAlertSender(
    INotificationSender notificationSender,
    IHttpContextService httpContextService,
    IOptions<ApplicationOptions> applicationOptions,
    TimeProvider timeProvider,
    ILogger<SecurityAlertSender> logger) : ISecurityAlertSender
{
    public async Task SendAsync(ApplicationUser user, string eventKey, string? detail, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(user.Email) || !user.EmailConfirmed ||
            user.Email.EndsWith(ApplicationUser.PhonePlaceholderEmailDomain, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var model = new SecurityAlertModel
        {
            EventKey = eventKey,
            Detail = detail,
            OccurredAt = timeProvider.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture),
            IpAddress = httpContextService.GetIpAddress(),
            ManageUrl = applicationOptions.Value.FrontendBaseUrl.TrimEnd('/') + "/account/security"
        };

        try
        {
            await notificationSender
                .ToEmail(user.Email, NotificationCultures.FromCurrentUICulture(), user.Id)
                .WithEmail(EmailTemplate.SecurityAlert, model.ToDictionary())
                .SendAsync(NotificationChannel.Email, ct);
        }
#pragma warning disable CA1031 // Best effort by design: the security change is committed and audited already.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            logger.LogError(ex, "Security alert {EventKey} could not be enqueued for user {UserId}", eventKey, user.Id);
        }
    }
}
