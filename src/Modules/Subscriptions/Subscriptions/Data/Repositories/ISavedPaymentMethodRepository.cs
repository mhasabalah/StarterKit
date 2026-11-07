using Subscriptions.Domains.OnlinePayments.Models;

namespace Subscriptions.Data.Repositories;

/// <summary>Repository of the <see cref="SavedPaymentMethod"/> aggregate (a subscriber's saved cards).</summary>
public interface ISavedPaymentMethodRepository
{
    /// <summary>Tracked method of the subscriber (revoked ones too), or null. Never another subscriber's.</summary>
    Task<SavedPaymentMethod?> GetAsync(Guid subscriberId, Guid id, CancellationToken ct = default);

    /// <summary>Tracked method by the provider's id, or null.</summary>
    Task<SavedPaymentMethod?> GetByProviderMethodAsync(string gateway, string providerMethodRef, CancellationToken ct = default);

    /// <summary>The subscriber's active default method, or null.</summary>
    Task<SavedPaymentMethod?> GetDefaultAsync(Guid subscriberId, CancellationToken ct = default);

    /// <summary>Tracked active methods of the subscriber, newest first.</summary>
    Task<IReadOnlyList<SavedPaymentMethod>> GetActiveAsync(Guid subscriberId, CancellationToken ct = default);

    void Add(SavedPaymentMethod method);

    /// <summary>A transaction for a change that spans several saves (the default flips from one method to another).</summary>
    Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
