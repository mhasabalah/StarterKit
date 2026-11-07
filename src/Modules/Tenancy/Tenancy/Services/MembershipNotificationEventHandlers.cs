using System.Resources;
using Notification.Contracts;
using Notification.Contracts.Enums;
using Notification.Contracts.InApp;
using Shared.Core.Localization;
using Shared.Core.Web;
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

    /// <summary>
    /// Sends inside the tenant's scope (the inbox row and delivery jobs carry it), never to the person who made the change
    /// themselves, and never fails the caller.
    /// </summary>
    public static async Task NotifyAsync(
        ITenantStore tenants, IHttpContextService http, INotificationSender notifications,
        Guid tenantId, Guid memberId, string eventKey, CancellationToken ct)
    {
        if (http.GetUserIdAsGuid() == memberId)
            return; // their own action: they know

        var tenant = await tenants.FindByIdAsync(tenantId, ct);
        if (tenant is null)
            return;

        using (TenantScope.Begin(tenant))
        {
            await notifications.For(memberId).WithInApp(Content(eventKey, tenant.Name, tenantId)).SendAsync(ct: ct);
        }
    }
}

/// <summary>A new (non-owner) member joined (e.g. added by an admin or a directory): tell that member.</summary>
public sealed class TenantMemberJoinedNotificationEventHandler(
    TenancyDbContext dbContext,
    ITenantStore tenants,
    IHttpContextService http,
    INotificationSender notifications,
    ILogger<TenantMemberJoinedNotificationEventHandler> logger) : INotificationHandler<TenantMemberJoinedEvent>
{
    public async Task Handle(TenantMemberJoinedEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            bool isOwner;
            using (TenantScope.BeginHostScope()) // control-plane read only
            {
                var membership = await dbContext.Memberships.AsNoTracking()
                    .Where(m => m.TenantId == notification.TenantId && m.UserId == notification.UserId)
                    .Select(m => new { m.IsOwner })
                    .FirstOrDefaultAsync(cancellationToken);
                if (membership is null)
                    return;
                isOwner = membership.IsOwner;
            }

            // The owner "joins" at tenant creation; telling them is noise.
            if (isOwner)
                return;

            await MembershipInApp.NotifyAsync(tenants, http, notifications, notification.TenantId, notification.UserId,
                "joined", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not notify user {UserId} about joining tenant {TenantId}", notification.UserId, notification.TenantId);
        }
    }
}

/// <summary>A member's roles changed: tell that member (other membership changes - branches, suspension - stay silent).</summary>
public sealed class TenantMemberRolesChangedNotificationEventHandler(
    ITenantStore tenants,
    IHttpContextService http,
    INotificationSender notifications,
    ILogger<TenantMemberRolesChangedNotificationEventHandler> logger) : INotificationHandler<TenantMembershipChangedEvent>
{
    public async Task Handle(TenantMembershipChangedEvent notification, CancellationToken cancellationToken)
    {
        if (notification is not { Removed: false, RolesChanged: true })
            return;

        try
        {
            await MembershipInApp.NotifyAsync(tenants, http, notifications, notification.TenantId, notification.UserId,
                "role-changed", cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not notify user {UserId} about a role change in tenant {TenantId}", notification.UserId, notification.TenantId);
        }
    }
}
