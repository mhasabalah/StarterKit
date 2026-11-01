namespace Subscriptions.Data.Repositories;

public class PaymentInstructionsRepository(SubscriptionsDbContext context)
    : IPaymentInstructionsRepository
{
    public async Task<PaymentInstructions?> GetAsync(CancellationToken ct = default)
        => await context.PaymentInstructions.FirstOrDefaultAsync(ct);

    public async Task AddAsync(PaymentInstructions instructions, CancellationToken ct = default)
    {
        await context.PaymentInstructions.AddAsync(instructions, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task SaveChangesAsync(CancellationToken ct = default)
        => await context.SaveChangesAsync(ct);
}
