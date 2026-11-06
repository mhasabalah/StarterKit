namespace Subscriptions.Contracts.Subscribers;

/// <summary>
/// Who "the subscriber" is for the current request. Single-tenant default: the signed-in user. Multi-tenant
/// (registered by the Tenancy module when <c>MultiTenancy:Enabled</c>): the current TENANT - the company pays,
/// and only members allowed to manage billing may act for it.
/// </summary>
public interface ISubscriberIdProvider
{
    /// <summary>
    /// The current subscriber id for MANAGING the subscription (checkout, invoices, refunds), or <c>null</c> when there
    /// is none (anonymous / no tenant). May throw when the caller may not act for the subscriber (e.g. a tenant member
    /// without the billing permission).
    /// </summary>
    Guid? GetCurrentSubscriberId();

    /// <summary>
    /// The subscriber whose PLAN governs the current flow, for entitlement and metering reads (feature gates, quotas).
    /// Never requires a billing permission: any member's request (or background work for them) is paid by the
    /// tenant. Defaults to <see cref="GetCurrentSubscriberId"/>.
    /// </summary>
    Guid? GetPayingSubscriberId() => GetCurrentSubscriberId();
}
