using Notification.Contracts;
using Notification.Contracts.Email.Models;
using Notification.Contracts.Enums;
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
