namespace Subscriptions.Domains.Subscriptions.Services;

public interface IInvoiceService
{
    Task<SubscriptionInvoice> GenerateActivationInvoiceAsync(
        Subscription subscription,
        Guid paymentId,
        CancellationToken ct = default);

    Task<SubscriptionInvoice> GenerateRenewalInvoiceAsync(
        Subscription subscription,
        Guid paymentId,
        CancellationToken ct = default);

    Task<SubscriptionInvoice> GenerateProratedInvoiceAsync(
        Subscription subscription,
        Guid? paymentId,
        decimal amount,
        bool isDeferred,
        Guid originalPlanId,
        CancellationToken ct = default);

    Task RetryAsync(
        SubscriptionInvoice invoice,
        Subscription subscription,
        CancellationToken ct = default);

    Task ResendAsync(
        SubscriptionInvoice invoice,
        Subscription subscription,
        CancellationToken ct = default);
}
