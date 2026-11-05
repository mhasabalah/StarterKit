using MassTransit;
using Shared.Contracts.Messaging;

namespace Subscriptions.Contracts.Subscriptions.IntegrationEvents;

/// <summary>
/// Integration event: a subscription became Active (first activation or reactivation after payment). Raised by the
/// Subscription aggregate into the Subscriptions outbox in the same transaction.
/// <para>
/// A TRIGGER, not the state: consumers re-read the subscriber's current status
/// (<c>GetSubscriberSubscriptionStatusQuery</c>) so an Activated delivered after a later Expired (or vice versa) can
/// never leave them in the wrong state.
/// </para>
/// </summary>
[MessageUrn("subscriptions.subscription-activated.v1")]
[PublicWebhookEvent(Description = "A subscription became active.")]
public sealed class SubscriptionActivatedIntegrationEvent : IntegrationEvent
{
    public Guid SubscriptionId { get; init; }
    public Guid SubscriberId { get; init; }
}

/// <summary>
/// Integration event: a subscription expired (grace period over). Same trigger semantics as
/// <see cref="SubscriptionActivatedIntegrationEvent"/>.
/// </summary>
[MessageUrn("subscriptions.subscription-expired.v1")]
[PublicWebhookEvent(Description = "A subscription expired.")]
public sealed class SubscriptionExpiredIntegrationEvent : IntegrationEvent
{
    public Guid SubscriptionId { get; init; }
    public Guid SubscriberId { get; init; }
}
