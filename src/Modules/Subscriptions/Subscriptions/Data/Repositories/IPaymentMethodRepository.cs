namespace Subscriptions.Data.Repositories;

public interface IPaymentMethodRepository
{
    Task<PaymentMethod?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyList<PaymentMethod>> GetAllAsync(CancellationToken ct = default);
    Task AddAsync(PaymentMethod method, CancellationToken ct = default);
    Task DeleteAsync(PaymentMethod method, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
