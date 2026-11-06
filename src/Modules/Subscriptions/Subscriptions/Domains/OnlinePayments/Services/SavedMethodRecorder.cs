using Subscriptions.Domains.OnlinePayments.Gateways;
using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Domains.OnlinePayments.Services;

/// <summary>
/// Stores the payment method a settled checkout reported (the subscriber agreed to save it at the provider). Runs AFTER
/// the settlement committed and never fails it: a card that cannot be saved just means the next overdue payment is
/// chased by email instead of an automatic charge.
/// </summary>
internal interface ISavedMethodRecorder
{
    Task RecordAsync(
        string provider, Guid subscriberId, SavedMethodCapture capture, string? paymentReference, CancellationToken ct);
}

internal sealed class SavedMethodRecorder(
    ISavedPaymentMethodRepository methods,
    IPaymentGatewayRegistry gateways,
    TimeProvider timeProvider,
    ILogger<SavedMethodRecorder> logger) : ISavedMethodRecorder
{
    public async Task RecordAsync(
        string provider, Guid subscriberId, SavedMethodCapture capture, string? paymentReference, CancellationToken ct)
    {
        try
        {
            var gateway = gateways.Get(provider);
            var complete = await gateway.DescribeSavedMethodAsync(capture, paymentReference, ct);
            if (complete is not { MethodRef: { Length: > 0 } methodRef })
                return;

            var existing = await methods.GetByProviderMethodAsync(gateway.Name, methodRef, ct);
            if (existing is not null)
            {
                if (existing.SubscriberId != subscriberId)
                {
                    logger.LogWarning("Provider method of {Gateway} is already saved by another subscriber; ignoring.", gateway.Name);
                    return;
                }

                existing.Reactivate(complete.Brand, complete.Last4, complete.ExpMonth, complete.ExpYear);
            }
            else
            {
                // The first method a subscriber saves becomes the default; later ones are chosen by the subscriber.
                var hasDefault = await methods.GetDefaultAsync(subscriberId, ct) is not null;
                methods.Add(SavedPaymentMethod.Create(
                    subscriberId, gateway.Name, complete.CustomerRef, methodRef, complete.Brand, complete.Last4,
                    complete.ExpMonth, complete.ExpYear, isDefault: !hasDefault, timeProvider.GetUtcNow()));
            }

            await methods.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Could not save the payment method reported by {Provider} for subscriber {SubscriberId}.",
                provider, subscriberId);
        }
    }
}
