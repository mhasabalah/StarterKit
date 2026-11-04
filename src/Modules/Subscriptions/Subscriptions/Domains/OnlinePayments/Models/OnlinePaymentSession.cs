namespace Subscriptions.Domains.OnlinePayments.Models;

/// <summary>
/// One hosted-checkout attempt for a <see cref="SubscriptionPayment"/>: the link the webhook uses to find the payment
/// from the provider's session id. <see cref="Amount"/>/<see cref="Currency"/> are what was asked from the provider
/// (the payment's outstanding amount at that moment); a webhook must report exactly that.
/// </summary>
public class OnlinePaymentSession
{
    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public string Provider { get; private set; } = default!;

    /// <summary>The provider's session / charge id (unique per provider).</summary>
    public string ProviderReference { get; private set; } = default!;

    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public OnlinePaymentSessionStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private OnlinePaymentSession()
    {
    }

    public static OnlinePaymentSession Create(
        SubscriptionPayment payment,
        string provider,
        string providerReference,
        decimal amount,
        DateTimeOffset now) => new()
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            SubscriptionId = payment.SubscriptionId,
            Provider = provider,
            ProviderReference = providerReference,
            Amount = amount,
            Currency = payment.Currency,
            Status = OnlinePaymentSessionStatus.Created,
            CreatedAt = now
        };

    public void MarkSucceeded(DateTimeOffset now) => Complete(OnlinePaymentSessionStatus.Succeeded, now);

    /// <summary>A failure report never downgrades a session that already succeeded.</summary>
    public void MarkFailed(DateTimeOffset now)
    {
        if (Status == OnlinePaymentSessionStatus.Created)
            Complete(OnlinePaymentSessionStatus.Failed, now);
    }

    public void MarkRefunded(DateTimeOffset now) => Complete(OnlinePaymentSessionStatus.Refunded, now);

    private void Complete(OnlinePaymentSessionStatus status, DateTimeOffset now)
    {
        Status = status;
        CompletedAt = now;
    }
}
