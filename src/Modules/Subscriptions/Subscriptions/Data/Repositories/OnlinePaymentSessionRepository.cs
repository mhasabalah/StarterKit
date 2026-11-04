using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Data.Repositories;

public class OnlinePaymentSessionRepository(SubscriptionsDbContext context) : IOnlinePaymentSessionRepository
{
    public Task<OnlinePaymentSession?> GetByProviderReferenceAsync(
        string provider, string providerReference, CancellationToken ct = default)
        => context.OnlinePaymentSessions.FirstOrDefaultAsync(
            x => x.Provider == provider && x.ProviderReference == providerReference, ct);

    public async Task AddAsync(OnlinePaymentSession session, CancellationToken ct = default)
    {
        await context.OnlinePaymentSessions.AddAsync(session, ct);
        await context.SaveChangesAsync(ct);
    }
}
