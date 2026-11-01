namespace Subscriptions.Data.Repositories;

public interface IPaymentInstructionsRepository
{
    Task<PaymentInstructions?> GetAsync(CancellationToken ct = default);
    Task AddAsync(PaymentInstructions instructions, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
