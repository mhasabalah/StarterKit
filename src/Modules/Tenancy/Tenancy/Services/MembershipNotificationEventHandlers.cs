using System.Resources;
using Notification.Contracts;
using Notification.Contracts.Enums;
using Notification.Contracts.InApp;
using Shared.Core.Localization;
using Tenancy.Resources;

namespace Tenancy.Services;

/// <summary>
/// Tells a person about changes to THEIR membership (inbox + real-time, no push: informational). Best effort, after
/// the change committed; a notification problem never fails the membership change. Category group <c>membership</c>.
/// </summary>
internal static class MembershipInApp
{
    private static readonly ResourceManager Resources = new(typeof(TenancyResource));

    public static InAppContent Content(string eventKey, string tenantName, Guid tenantId) =>
        new(
            Title: BilingualResource.Get(Resources, $"InApp.membership.{eventKey}.Title"),
            Body: BilingualResource.Get(Resources, $"InApp.membership.{eventKey}.Body", tenantName),
            Category: $"membership.{eventKey}",
            ActionUrl: null,
            Severity: NotificationSeverity.Info,
            Data: new Dictionary<string, string> { ["tenantId"] = tenantId.ToString() });

    public static async Task SendAsync(INotificationSender notifications, Guid userId, InAppContent content, CancellationToken ct) =>
        await notifications.For(userId).WithInApp(content).SendAsync(ct: ct);
}

/// <summary>A new (non-owner) member joined - e.g. accepted an invitation: tell that member.</summary>
public sealed class TenantMemberJoinedNotificationEventHandler(
    TenancyDbContext dbContext,
    INotificationSender notifications,
    ILogger<TenantMemberJoinedNotificationEventHandler> logger) : INotificationHandler<TenantMemberJoinedEvent>
{
    public async Task Handle(TenantMemberJoinedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            using (TenantScope.BeginHostScope())
            {
                var membership = await dbContext.Memberships.AsNoTracking()
                    .Where(m => m.TenantId == notification.TenantId && m.UserId == notification.UserId)
                    .Select(m => new { m.IsOwner })
                    .FirstOrDefaultAsync(cancellationToken);
                // The owner "joins" at tenant creation; telling them is noise.
                if (membership is null or { IsOwner: true })
                    return;

                var name = await dbContext.Tenants.AsNoTracking()
                    .Where(t => t.Id == notification.TenantId).Select(t => t.Name)
                    .FirstOrDefaultAsync(cancellationToken);
                if (name is null)
                    return;

                await MembershipInApp.SendAsync(notifications, notification.UserId,
                    MembershipInApp.Content("joined", name, notification.TenantId), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not notify user {UserId} about joining tenant {TenantId}", notification.UserId, notification.TenantId);
        }
    }
}

/// <summary>A member's roles changed: tell that member (other membership changes - branches, suspension - stay silent).</summary>
public sealed class TenantMemberRolesChangedNotificationEventHandler(
    TenancyDbContext dbContext,
    INotificationSender notifications,
    ILogger<TenantMemberRolesChangedNotificationEventHandler> logger) : INotificationHandler<TenantMembershipChangedEvent>
{
    public async Task Handle(TenantMembershipChangedEvent notification, CancellationToken cancellationToken)
    {
        if (notification is not { Removed: false, RolesChanged: true })
            return;

        try
        {
            using (TenantScope.BeginHostScope())
            {
                var name = await dbContext.Tenants.AsNoTracking()
                    .Where(t => t.Id == notification.TenantId).Select(t => t.Name)
                    .FirstOrDefaultAsync(cancellationToken);
                if (name is null)
                    return;

                await MembershipInApp.SendAsync(notifications, notification.UserId,
                    MembershipInApp.Content("role-changed", name, notification.TenantId), cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not notify user {UserId} about a role change in tenant {TenantId}", notification.UserId, notification.TenantId);
        }
    }
}
