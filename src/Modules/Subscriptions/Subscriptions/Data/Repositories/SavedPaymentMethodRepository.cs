using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Data.Repositories;

public class SavedPaymentMethodRepository(SubscriptionsDbContext context) : ISavedPaymentMethodRepository
{
    public Task<SavedPaymentMethod?> GetAsync(Guid subscriberId, Guid id, CancellationToken ct = default)
        => context.SavedPaymentMethods.FirstOrDefaultAsync(x => x.Id == id && x.SubscriberId == subscriberId, ct);

    public Task<SavedPaymentMethod?> GetByProviderMethodAsync(
        string gateway, string providerMethodRef, CancellationToken ct = default)
        => context.SavedPaymentMethods.FirstOrDefaultAsync(
            x => x.Gateway == gateway && x.ProviderMethodRef == providerMethodRef, ct);

    public Task<SavedPaymentMethod?> GetDefaultAsync(Guid subscriberId, CancellationToken ct = default)
        => context.SavedPaymentMethods.FirstOrDefaultAsync(
            x => x.SubscriberId == subscriberId && x.IsDefault && x.RevokedAt == null, ct);

    public async Task<IReadOnlyList<SavedPaymentMethod>> GetActiveAsync(Guid subscriberId, CancellationToken ct = default)
        => await context.SavedPaymentMethods
            .Where(x => x.SubscriberId == subscriberId && x.RevokedAt == null)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync(ct);

    public void Add(SavedPaymentMethod method) => context.SavedPaymentMethods.Add(method);

    public Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default)
        => context.Database.BeginTransactionAsync(ct);

    public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
