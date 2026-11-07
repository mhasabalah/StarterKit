using Identity.Domains.Sso.Events;
using Notification.Contracts.Email.Models;
using Notification.Contracts.InApp;
using System.Globalization;
using Shared.Contracts.Localization;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Authorization;
using Tenancy.Contracts.Members;
using Tenancy.Contracts.Roles;
using Tenancy.Contracts.Tenants;

namespace Identity.Domains.Sso.EventHandlers;

/// <summary>
/// An IdP signing certificate is about to expire: tell the people who can fix it, i.e. the ACTIVE tenant members who
/// hold <c>tenant.sso.manage</c> (the owner and every member of a role that carries it), by email and in the in-app
/// inbox (category <see cref="Category"/>). Recipients are resolved through the Tenancy contracts, in the tenant's
/// scope: one members query per admin role (paged) plus the owner. Delivery failures PROPAGATE: the job that published
/// the event releases its claim on the alert, so the next run retries.
/// </summary>
public sealed class SsoCertificateExpiringEventHandler(
    ISender sender,
    ITenantStore tenantStore,
    INotificationSender notifications,
    ILogger<SsoCertificateExpiringEventHandler> logger) : INotificationHandler<SsoCertificateExpiringEvent>
{
    public const string Category = "security.sso-certificate-expiring";
    private const int PageSize = 100;
    private const int MaxPages = 50;

    public async Task Handle(SsoCertificateExpiringEvent notification, CancellationToken cancellationToken)
    {
        var tenant = await tenantStore.FindByIdAsync(notification.TenantId, cancellationToken);
        if (tenant is null)
        {
            logger.LogWarning("SSO certificate of connection {ConnectionId} expires but tenant {TenantId} was not found",
                notification.ConnectionId, notification.TenantId);
            return;
        }

        IReadOnlyList<Guid> recipients;
        using (TenantScope.Begin(tenant))
        {
            recipients = await ResolveAdminsAsync(cancellationToken);
        }

        if (recipients.Count == 0)
        {
            logger.LogWarning("SSO certificate of connection {ConnectionId} expires but tenant {TenantId} has no SSO admin to notify",
                notification.ConnectionId, notification.TenantId);
            return;
        }

        var model = new SsoCertificateExpiringModel
        {
            ConnectionName = notification.ConnectionName,
            ExpiresOn = notification.NotAfter.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            DaysLeft = notification.DaysLeft
        };
        var inApp = Content(notification);

        // The tenant's culture picks the email language (the inbox entry is bilingual and follows the user).
        var culture = tenant.Culture?.StartsWith("ar", StringComparison.OrdinalIgnoreCase) == true
            ? NotificationCulture.Ar
            : NotificationCulture.En;
        foreach (var userId in recipients)
        {
            await notifications
                .For(userId, culture)
                .WithEmail(EmailTemplate.SsoCertificateExpiring, model.ToDictionary())
                .WithInApp(inApp)
                .SendAsync(ct: cancellationToken);
        }
    }

    private async Task<IReadOnlyList<Guid>> ResolveAdminsAsync(CancellationToken ct)
    {
        var roles = await sender.Send(new GetTenantRolesQuery(), ct);
        var recipients = new HashSet<Guid>();

        // The owner always can; it sorts first in an unfiltered members list.
        var first = await sender.Send(new GetMembersQuery
        {
            Status = TenantMembershipStatus.Active,
            PageNumber = 1,
            PageSize = PageSize
        }, ct);
        foreach (var owner in (first.Result ?? []).Where(m => m.IsOwner))
            recipients.Add(owner.UserId);

        foreach (var role in roles.Where(r => r.Permissions.Contains(TenantPermissions.SsoManage, StringComparer.Ordinal)))
        {
            for (var page = 1; page <= MaxPages; page++)
            {
                var members = await sender.Send(new GetMembersQuery
                {
                    Status = TenantMembershipStatus.Active,
                    RoleId = role.Id,
                    PageNumber = page,
                    PageSize = PageSize
                }, ct);

                foreach (var member in members.Result ?? [])
                    recipients.Add(member.UserId);

                var info = members.PageInfoResult;
                var pageSize = Math.Max(1, info.PageSize); // the server may clamp the requested size
                if (page * pageSize >= info.TotalItems)
                    break;
            }
        }

        return recipients.ToList();
    }

    public static InAppContent Content(SsoCertificateExpiringEvent e) => new(
        Title: new LocalizedText("SSO certificate expiring soon", "شهادة SSO توشك على الانتهاء"),
        Body: new LocalizedText(
            $"The identity provider certificate of \"{e.ConnectionName}\" expires in {e.DaysLeft} days. Update the connection before sign-in stops working.",
            $"تنتهي صلاحية شهادة موفر الهوية للاتصال \"{e.ConnectionName}\" خلال {e.DaysLeft} يومًا. حدّث الاتصال قبل أن يتوقف تسجيل الدخول."),
        Category: Category,
        ActionUrl: "/settings/sso",
        Severity: e.DaysLeft <= 7 ? NotificationSeverity.Critical : NotificationSeverity.Warning,
        Data: new Dictionary<string, string>
        {
            ["connectionId"] = e.ConnectionId.ToString(),
            ["notAfter"] = e.NotAfter.ToString("O"),
            ["daysLeft"] = e.DaysLeft.ToString(CultureInfo.InvariantCulture)
        });
}
