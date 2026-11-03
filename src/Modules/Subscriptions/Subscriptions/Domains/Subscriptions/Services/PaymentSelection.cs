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
}
