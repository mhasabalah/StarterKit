namespace Subscriptions.Contracts.Subscribers;

/// <summary>
/// Who "the subscriber" is for the current request. Single-tenant default: the signed-in user. Multi-tenant
/// (registered by the Tenancy module when <c>MultiTenancy:Enabled</c>): the current TENANT - the company pays,
/// and only members allowed to manage billing may act for it.
/// </summary>
public interface ISubscriberIdProvider
{
    /// <summary>The current subscriber id, or <c>null</c> when there is none (anonymous / no tenant).</summary>
    Guid? GetCurrentSubscriberId();
}
