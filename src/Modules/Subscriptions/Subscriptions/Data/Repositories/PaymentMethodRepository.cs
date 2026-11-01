namespace Subscriptions.Data.Repositories;

public class PaymentMethodRepository(SubscriptionsDbContext context)
    : IPaymentMethodRepository
{
    public async Task<PaymentMethod?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => await context.PaymentMethods.FirstOrDefaultAsync(x => x.Id == id, ct);

    public async Task<IReadOnlyList<PaymentMethod>> GetAllAsync(CancellationToken ct = default)
        => await context.PaymentMethods
            .OrderBy(x => x.SortOrder)
            .ToListAsync(ct);

    public async Task AddAsync(PaymentMethod method, CancellationToken ct = default)
    {
        await context.PaymentMethods.AddAsync(method, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task DeleteAsync(PaymentMethod method, CancellationToken ct = default)
    {
        context.PaymentMethods.Remove(method);
        await context.SaveChangesAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await context.SaveChangesAsync(ct);
}
