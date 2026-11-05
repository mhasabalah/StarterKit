using Webhooks.Domains.Deliveries.Models;

namespace Webhooks.Data.Repository;

public interface IWebhookDeliveryRepository
{
    Task<WebhookDelivery?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(WebhookDelivery delivery, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}

public class WebhookDeliveryRepository(WebhooksDbContext context) : IWebhookDeliveryRepository
{
    public Task<WebhookDelivery?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Deliveries.FirstOrDefaultAsync(d => d.Id == id, cancellationToken);

    public async Task AddAsync(WebhookDelivery delivery, CancellationToken cancellationToken = default) =>
        await context.Deliveries.AddAsync(delivery, cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);
}
