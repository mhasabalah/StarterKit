using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Data.Repositories;

public interface IOnlinePaymentSessionRepository
{
    /// <summary>Tracked session for a provider's session/charge id, or null.</summary>
    Task<OnlinePaymentSession?> GetByProviderReferenceAsync(string provider, string providerReference, CancellationToken ct = default);

    Task AddAsync(OnlinePaymentSession session, CancellationToken ct = default);
}
