using Notification.Contracts;
using Notification.Contracts.Email.Models;
using Notification.Contracts.Enums;
using Notification.Contracts.InApp;
using System.Resources;
using Shared.Core.Localization;
using Webhooks.Resources;
using Tenancy.Contracts.Tenants;
using Webhooks.Domains.Endpoints.Events;

namespace Webhooks.Domains.Endpoints.EventHandlers;

/// <summary>
/// Tells the tenant OWNER that an endpoint was switched off (dispatched after the disabling unit of work committed).
/// The owner comes from Tenancy through <c>ISender</c> + <c>Tenancy.Contracts</c>; the mail through
/// <see cref="INotificationSender"/>. Best effort: a mail problem must not fail the delivery bookkeeping.
/// </summary>
public sealed class WebhookEndpointAutoDisabledEventHandler(
    ISender sender,
    INotificationSender notifications,
    ITenantContext tenantContext,
    ILogger<WebhookEndpointAutoDisabledEventHandler> logger)
    : INotificationHandler<WebhookEndpointAutoDisabledEvent>
{
    public const string Category = "webhooks.endpoint-disabled";

    private static readonly ResourceManager Resources = new(typeof(WebhooksResource));

    /// <summary>The inbox entry (and push text): ids and the endpoint address only, in both languages.</summary>
    public static InAppContent InApp(WebhookEndpointAutoDisabledEvent e) =>
        new(
            Title: BilingualResource.Get(Resources, "InApp.webhooks.endpoint-disabled.Title"),
            Body: BilingualResource.Get(Resources, "InApp.webhooks.endpoint-disabled.Body", e.Url, e.ConsecutiveFailures),
            Category: Category,
            ActionUrl: "/settings/webhooks",
            Severity: NotificationSeverity.Warning,
            Data: new Dictionary<string, string> { ["endpointId"] = e.EndpointId.ToString() });

    public async Task Handle(WebhookEndpointAutoDisabledEvent notification, CancellationToken cancellationToken)
    {
        try
        {
            if (tenantContext.TenantId is not { } tenantId)
            {
                return;
            }

            var tenant = await sender.Send(new GetTenantByIdQuery(tenantId), cancellationToken);
            if (tenant is null)
            {
                logger.LogWarning("Webhook endpoint {EndpointId} disabled but tenant {TenantId} was not found; owner not notified",
                    notification.EndpointId, tenantId);
                return;
            }

            var model = new WebhookEndpointDisabledModel
            {
                TenantName = tenant.Name,
                EndpointAddress = notification.Url,
                ConsecutiveFailures = notification.ConsecutiveFailures,
                LastError = notification.Reason
            };

            await notifications
                .For(tenant.OwnerUserId)
                .WithEmail(EmailTemplate.WebhookEndpointDisabled, model.ToDictionary())
                .WithInApp(InApp(notification))
                .WithPush()
                .SendAsync(NotificationChannel.Email, cancellationToken);

            logger.LogWarning("Webhook endpoint {EndpointId} auto-disabled after {Failures} failures; owner {OwnerId} notified",
                notification.EndpointId, notification.ConsecutiveFailures, tenant.OwnerUserId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Could not notify the owner that webhook endpoint {EndpointId} was disabled",
                notification.EndpointId);
        }
    }
}
