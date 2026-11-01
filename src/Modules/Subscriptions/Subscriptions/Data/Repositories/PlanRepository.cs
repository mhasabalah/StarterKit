namespace Subscriptions.Data.Repositories;

public class PlanRepository(SubscriptionsDbContext context) : IPlanRepository
{
    public async Task<Plan?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.Plans
            .Include(x => x.Features)
            .FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<bool> HasActiveSubscriptionsAsync(Guid planId, CancellationToken ct = default)
        => await context.Subscriptions.AnyAsync(x =>
                x.PlanId == planId &&
                x.Status != SubscriptionStatus.Expired &&
                x.Status != SubscriptionStatus.Cancelled &&
                x.Status != SubscriptionStatus.Rejected,
            ct);

    public async Task AddAsync(Plan plan, CancellationToken ct = default)
    {
        await context.Plans.AddAsync(plan, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Plan plan, CancellationToken ct = default)
    {
        await context.SaveChangesAsync(ct);
    }
}
