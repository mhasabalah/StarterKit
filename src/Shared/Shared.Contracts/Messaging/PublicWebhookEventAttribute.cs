namespace Shared.Contracts.Messaging;

/// <summary>
/// Marks an <see cref="IIntegrationEvent"/> as part of the PUBLIC webhook catalog: tenants may subscribe their
/// webhook endpoints to it and the Webhooks module forwards it (signed, with retries) to their URLs.
/// <para>
/// Opting in is a public API commitment: the event's wire name (<c>[MessageUrn]</c>) becomes the webhook event type
/// customers integrate against, and its JSON shape is delivered to third parties. Evolve it additively only
/// (see <c>wiki/Architecture/Messaging.md</c>). Never mark events that carry personal or internal data.
/// </para>
/// Each marked event needs a forwarding consumer in the Webhooks module (guarded by a Webhooks unit test).
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class PublicWebhookEventAttribute : Attribute
{
    /// <summary>Optional one-line description shown in the event-type catalog.</summary>
    public string? Description { get; init; }
}
