namespace Subscriptions.Data.Repositories;

public interface IPlanRepository
{
    Task<Plan?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<bool> HasActiveSubscriptionsAsync(Guid planId, CancellationToken ct = default);
    Task AddAsync(Plan plan, CancellationToken ct = default);
    Task UpdateAsync(Plan plan, CancellationToken ct = default);
}
