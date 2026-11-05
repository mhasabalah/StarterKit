using UserDirectory.Contracts.Queries;
using Subscriptions.Options;

namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>
/// Who receives admin billing emails ("new payment proof", "refund requested", daily digest):
/// <list type="number">
/// <item><c>Subscriptions:AdminNotifications:Emails</c> when not empty;</item>
/// <item>otherwise the active users of <c>Subscriptions:AdminNotifications:Roles</c> (default Admin + Finance),
/// resolved through the user-directory port <see cref="GetUsersInRoleQuery"/>.</item>
/// </list>
/// Never the sender address (<c>Email:FromAddress</c>). An empty result means "nobody configured": callers log and skip.
/// </summary>
public interface IAdminNotificationRecipients
{
    Task<IReadOnlyList<string>> GetEmailsAsync(CancellationToken ct = default);
}

public sealed class AdminNotificationRecipients(
    IOptions<SubscriptionOptions> options,
    ISender sender,
    ILogger<AdminNotificationRecipients> logger)
    : IAdminNotificationRecipients
{
    private const int MaxRecipientsPerRole = 100;

    public async Task<IReadOnlyList<string>> GetEmailsAsync(CancellationToken ct = default)
    {
        var settings = options.Value.AdminNotifications;
        var configured = Normalize(settings.Emails ?? []);
        if (configured.Count > 0)
        {
            return configured;
        }

        var emails = new List<string?>();
        foreach (var role in settings.EffectiveRoles)
        {
            try
            {
                var users = await sender.Send(new GetUsersInRoleQuery(role, MaxRecipientsPerRole), ct);
                emails.AddRange(users.Select(x => x.Email));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Identity not wired (tests, a host without it) or a transient failure: notify whoever we have.
                logger.LogWarning(ex, "Could not resolve admin notification recipients for role {Role}", role);
            }
        }

        return Normalize(emails);
    }

    private static List<string> Normalize(IEnumerable<string?> emails) =>
        emails
            .Where(x => !string.IsNullOrWhiteSpace(x) && x.Contains('@'))
            .Select(x => x!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
