namespace Subscriptions.Domains.OnlinePayments.Models;

/// <summary>
/// Money the provider gave back for one settled <see cref="OnlinePaymentSession"/>. <c>(Provider, ProviderRefundId)</c>
/// is unique: the same refund reported twice (new event id, or an admin refund echoed by its own webhook) is booked
/// once. The sum per session is the cap for further refunds (never more than the money received).
/// </summary>
public class OnlinePaymentRefund
{
    public Guid Id { get; private set; }
    public Guid SessionId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public string Provider { get; private set; } = default!;
    public string ProviderRefundId { get; private set; } = default!;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;

    /// <summary>
    /// The admin refund request this refund fulfils; null for a refund the provider reported on its own (dashboard).
    /// Refunds with a request are already counted by the refund request itself.
    /// </summary>
    public Guid? RefundRequestId { get; private set; }

    /// <summary>
    /// True when the provider's webhook booked this refund (ledger entry + credit notes) on its own; a refund request
    /// that adopts it must not book that money a second time.
    /// </summary>
    public bool BookedByWebhook { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    private OnlinePaymentRefund()
    {
    }

    public static OnlinePaymentRefund Create(
        OnlinePaymentSession session,
        string providerRefundId,
        decimal amount,
        Guid? refundRequestId,
        DateTimeOffset now,
        bool bookedByWebhook = false)
    {
        if (amount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A refund must be positive.");

        return new OnlinePaymentRefund
        {
            Id = Guid.NewGuid(),
            SessionId = session.Id,
            SubscriptionId = session.SubscriptionId,
            Provider = session.Provider,
            ProviderRefundId = providerRefundId,
            Amount = amount,
            Currency = session.Currency,
            RefundRequestId = refundRequestId,
            BookedByWebhook = bookedByWebhook,
            CreatedAt = now
        };
    }

    /// <summary>An admin refund request adopts a refund the provider's webhook already booked on its own.</summary>
    public void LinkToRefundRequest(Guid refundRequestId) => RefundRequestId ??= refundRequestId;
}
