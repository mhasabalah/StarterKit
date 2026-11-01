namespace Subscriptions.Contracts.Subscribers;

/// <summary>
/// Resolves contact and billing details for a subscriber (the party that owns a subscription).
/// The Subscriptions module ships a default implementation that treats the subscriber as an
/// Identity user. Host applications whose subscribers are tenants/organizations can register
/// their own implementation; the default is registered with TryAdd so it is easy to override.
/// </summary>
public interface ISubscriberInfoProvider
{
    /// <summary>Returns the subscriber's details, or <c>null</c> when the subscriber cannot be resolved.</summary>
    Task<SubscriberInfo?> GetAsync(Guid subscriberId, CancellationToken cancellationToken = default);
}

/// <summary>Presentation-ready subscriber details used by invoices and notification emails.</summary>
/// <param name="SubscriberId">The subscriber identifier stored on the subscription.</param>
/// <param name="DisplayName">Name shown in emails and on invoices (person or organization name).</param>
/// <param name="Email">Primary contact email.</param>
/// <param name="Phone">Primary contact phone number.</param>
/// <param name="BillingName">Legal/billing name printed on invoices; falls back to <see cref="DisplayName"/>.</param>
/// <param name="BillingAddress">Billing address printed on invoices.</param>
/// <param name="TaxId">Tax/VAT registration number printed on invoices.</param>
public sealed record SubscriberInfo(
    Guid SubscriberId,
    string DisplayName,
    string? Email,
    string? Phone = null,
    string? BillingName = null,
    string? BillingAddress = null,
    string? TaxId = null);
