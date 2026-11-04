namespace Subscriptions.Domains.OnlinePayments.Models;

/// <summary>Lifecycle of a hosted-checkout attempt. Stored by name.</summary>
public enum OnlinePaymentSessionStatus
{
    /// <summary>Checkout created; the provider has not reported an outcome yet.</summary>
    Created = 0,

    /// <summary>The provider reported success and the payment was settled.</summary>
    Succeeded = 1,

    /// <summary>The provider reported a failed attempt (the payment stays owed; the subscriber may start a new checkout).</summary>
    Failed = 2,

    /// <summary>The provider reported a refund of a settled session (reconcile through the refund flow).</summary>
    Refunded = 3
}
