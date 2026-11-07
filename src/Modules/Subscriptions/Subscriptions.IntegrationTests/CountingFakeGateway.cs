using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Options;

namespace Subscriptions.IntegrationTests;

/// <summary>
/// The Fake gateway under the name "Fake", recording every call so tests can assert how often the provider was hit
/// (a re-run that "does nothing" must show zero extra charge calls, not just unchanged rows).
/// </summary>
internal sealed class CountingFakeGateway(IOptions<SubscriptionOptions> options) : IPaymentGateway
{
    private readonly FakePaymentGateway _inner = new(options);

    public static ConcurrentDictionary<string, int> ChargeCalls { get; } = new();
    public static ConcurrentDictionary<string, int> StatusCalls { get; } = new();
    public static ConcurrentBag<string> Detached { get; } = [];
    public static ConcurrentBag<CheckoutRequest> Checkouts { get; } = [];

    /// <summary>When true the gateway claims it cannot charge any amount (checkout / dunning must skip it).</summary>
    public static volatile bool RejectAmounts;

    public static int ChargesFor(string idempotencyKey) => ChargeCalls.GetValueOrDefault(idempotencyKey);

    public string Name => _inner.Name;

    public bool SupportsRefundIdempotency => true;

    public bool SupportsOffSessionCharges => true;

    public bool SupportsAmount(decimal amount, string currency) => !RejectAmounts;

    public Task<CheckoutSession> CreateCheckoutAsync(CheckoutRequest request, CancellationToken ct)
    {
        Checkouts.Add(request);
        return _inner.CreateCheckoutAsync(request, ct);
    }

    public WebhookParseResult ParseWebhook(WebhookRequest request) => _inner.ParseWebhook(request);

    public Task<RefundResult> RefundAsync(RefundRequest request, CancellationToken ct) => _inner.RefundAsync(request, ct);

    public Task<OffSessionChargeResult> ChargeSavedMethodAsync(OffSessionChargeRequest request, CancellationToken ct)
    {
        ChargeCalls.AddOrUpdate(request.IdempotencyKey, 1, (_, n) => n + 1);
        return _inner.ChargeSavedMethodAsync(request, ct);
    }

    public Task<OffSessionChargeResult> GetChargeStatusAsync(string providerReference, CancellationToken ct)
    {
        StatusCalls.AddOrUpdate(providerReference, 1, (_, n) => n + 1);
        return _inner.GetChargeStatusAsync(providerReference, ct);
    }

    public Task DetachAsync(string customerReference, string methodReference, CancellationToken ct)
    {
        Detached.Add(methodReference);
        return Task.CompletedTask;
    }

    public Task<SavedMethodCapture?> DescribeSavedMethodAsync(SavedMethodCapture partial, string? paymentReference, CancellationToken ct) =>
        ((IPaymentGateway)_inner).DescribeSavedMethodAsync(partial, paymentReference, ct);
}
