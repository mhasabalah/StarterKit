using Webhooks.Data.Configurations;
using Webhooks.Domains.Endpoints.Exceptions;
using Webhooks.Domains.Endpoints.Models;

namespace Webhooks.Data.Repository;

public interface IWebhookEndpointRepository
{
    Task<WebhookEndpoint?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task AddAsync(WebhookEndpoint endpoint, CancellationToken cancellationToken = default);

    void Remove(WebhookEndpoint endpoint);

    /// <summary>Saves; a unique-URL violation becomes <see cref="WebhookEndpointAlreadyExistsException"/>.</summary>
    Task SaveChangesAsync(string url, CancellationToken cancellationToken = default);
}

public class WebhookEndpointRepository(WebhooksDbContext context) : IWebhookEndpointRepository
{
    public Task<WebhookEndpoint?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.Endpoints.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        context.Endpoints.CountAsync(cancellationToken);

    public async Task AddAsync(WebhookEndpoint endpoint, CancellationToken cancellationToken = default) =>
        await context.Endpoints.AddAsync(endpoint, cancellationToken);

    public void Remove(WebhookEndpoint endpoint) => context.Endpoints.Remove(endpoint);

    public async Task SaveChangesAsync(string url, CancellationToken cancellationToken = default)
    {
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.IsUniqueViolation(WebhookEndpointConfiguration.UrlIndexName))
        {
            throw new WebhookEndpointAlreadyExistsException(url);
        }
    }
}
