namespace Subscriptions.Domains.OnlinePayments.Models;

public enum PaymentAttemptOutcome
{
    /// <summary>Recorded before the provider call: a row that stays here means the process died mid-call.</summary>
    Started = 0,
    Succeeded = 1,
    RequiresAction = 2,
    Declined = 3,

    /// <summary>A provider / network error: the outcome is unknown (the idempotency key makes a retry safe).</summary>
    Transient = 4
}

/// <summary>
/// One automatic (off-session) charge attempt for an overdue payment: at most one per (payment, dunning step), keyed
/// for the provider by <see cref="IdempotencyKey"/> (<c>dunning:{paymentId}:{step}</c>), so a crash or a re-run can
/// never charge a subscriber twice for the same step.
/// </summary>
public class PaymentAttempt
{
    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public string Step { get; private set; } = default!;
    public string IdempotencyKey { get; private set; } = default!;
    public Guid? SavedPaymentMethodId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = default!;
    public PaymentAttemptOutcome Outcome { get; private set; }

    /// <summary>The provider's charge / payment-intent id, once known.</summary>
    public string? ProviderReference { get; private set; }

    /// <summary>The provider's decline / failure code (e.g. <c>card_declined</c>, <c>authentication_required</c>).</summary>
    public string? FailureCode { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private PaymentAttempt()
    {
    }

    public static string KeyFor(Guid paymentId, string step) => $"dunning:{paymentId}:{step}";

    public static PaymentAttempt Start(
        SubscriptionPayment payment, string step, Guid savedPaymentMethodId, decimal amount, DateTimeOffset now)
    {
        return new PaymentAttempt
        {
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            SubscriptionId = payment.SubscriptionId,
            Step = step,
            IdempotencyKey = KeyFor(payment.Id, step),
            SavedPaymentMethodId = savedPaymentMethodId,
            Amount = amount,
            Currency = payment.Currency,
            Outcome = PaymentAttemptOutcome.Started,
            CreatedAt = now
        };
    }

    public void Complete(PaymentAttemptOutcome outcome, string? providerReference, string? failureCode, DateTimeOffset now)
    {
        if (outcome == PaymentAttemptOutcome.Started)
            throw new ArgumentException("An attempt completes with a result.", nameof(outcome));

        Outcome = outcome;
        ProviderReference = providerReference;
        FailureCode = failureCode is { Length: > 100 } ? failureCode[..100] : failureCode;
        CompletedAt = now;
    }
}
