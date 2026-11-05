namespace Webhooks.Domains.Endpoints.Events;

/// <summary>
/// The endpoint hit the consecutive-failure limit and was disabled. Handled in-module: the tenant owner is told by
/// email (after the unit of work commits, so a rolled-back disable never sends a mail).
/// </summary>
public sealed record WebhookEndpointAutoDisabledEvent(Guid EndpointId, string Url, int ConsecutiveFailures, string Reason)
    : IDomainEvent;
