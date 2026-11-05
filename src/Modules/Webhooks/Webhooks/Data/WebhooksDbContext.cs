using Webhooks.Domains.Deliveries.Models;
using Webhooks.Domains.Endpoints.Models;

namespace Webhooks.Data;

/// <summary>
/// DATA PLANE context (<see cref="ITenantDataContext"/>): every row is tenant-owned (named <c>Tenant</c> filter + forced
/// RLS). Webhooks consume integration events but publish none, so there is no outbox here.
/// </summary>
public class WebhooksDbContext(DbContextOptions<WebhooksDbContext> options)
    : AppDbContextBase(options), ITenantDataContext
{
    public DbSet<WebhookEndpoint> Endpoints => Set<WebhookEndpoint>();
    public DbSet<WebhookDelivery> Deliveries => Set<WebhookDelivery>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("webhooks");
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();

        // Last: tenant filter (named) and tenant_id indexes.
        ApplyTenantFilters(builder);
    }
}
