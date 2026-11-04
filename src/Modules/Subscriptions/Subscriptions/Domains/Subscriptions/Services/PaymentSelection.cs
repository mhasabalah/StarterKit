namespace Subscriptions.Domains.Subscriptions.Services;

/// <summary>
/// Binds an admin review (approve/reject) to ONE specific payment. When the client sends a PaymentId it must belong
/// to the subscription and have the expected purpose. Without one (older clients) the single submitted payment of
/// that purpose is used; none is a 409, several is a 400 (the admin must say which).
/// </summary>
public static class PaymentSelection
{
    public static async Task<SubscriptionPayment> ResolveSubmittedAsync(
        ISubscriptionRepository repository,
        Guid subscriptionId,
        Guid? paymentId,
        PaymentPurpose purpose,
        CancellationToken ct)
    {
        SubscriptionPayment payment;

        if (paymentId is { } id && id != Guid.Empty)
        {
            payment = await repository.GetPaymentAsync(subscriptionId, id, ct)
                ?? throw new PaymentNotFoundException(id);

            if (payment.Purpose != purpose)
                throw new BadRequestException($"Payment '{id}' is a {payment.Purpose} payment, not a {purpose} payment.");
        }
        else
        {
            var submitted = (await repository.GetAwaitingPaymentsAsync(subscriptionId, ct))
                .Where(x => x.Purpose == purpose && x.Status == PaymentStatus.Submitted)
                .ToList();

            payment = submitted.Count switch
            {
                0 => throw new ConflictException($"No submitted {purpose} payment is awaiting review."),
                1 => submitted[0],
                _ => throw new AmbiguousPaymentException()
            };
        }

        if (payment.Status != PaymentStatus.Submitted)
            throw new ConflictException($"Payment is {payment.Status}, not submitted for review.");

        return payment;
    }

    /// <summary>
    /// The payment the subscription owes in its current status (activation / renewal / proration). An explicit id
    /// must belong to the subscription; without one exactly one owed payment of that purpose must exist.
    /// </summary>
    public static async Task<SubscriptionPayment> ResolveOwedAsync(
        ISubscriptionRepository repository,
        Subscription subscription,
        Guid? paymentId,
        CancellationToken ct)
    {
        if (paymentId is { } id && id != Guid.Empty)
        {
            var explicitPayment = await repository.GetPaymentAsync(subscription.Id, id, ct)
                ?? throw new PaymentNotFoundException(id);

            if (!explicitPayment.IsAwaitingPayment)
                throw new ConflictException($"Payment is {explicitPayment.Status}; no amount is outstanding.");

            return explicitPayment;
        }

        var purpose = subscription.Status switch
        {
            SubscriptionStatus.PendingApproval => PaymentPurpose.Activation,
            SubscriptionStatus.PastDue => PaymentPurpose.Renewal,
            SubscriptionStatus.Active => PaymentPurpose.Proration,
            _ => throw new ConflictException($"A {subscription.Status} subscription owes no payment.")
        };

        var owed = (await repository.GetAwaitingPaymentsAsync(subscription.Id, ct))
            .Where(x => x.Purpose == purpose)
            .ToList();

        return owed.Count switch
        {
            0 => throw new ConflictException($"No {purpose} payment is outstanding."),
            1 => owed[0],
            _ => throw new AmbiguousPaymentException()
        };
    }
}
