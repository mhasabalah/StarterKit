using Shared.Core.Messaging.Outbox;
using Shipping.Domains.Declarations.Models;

namespace Shipping.Data;

/// <summary>
/// DATA PLANE context (<see cref="ITenantDataContext"/>): every row is tenant-owned (<see cref="IMustHaveTenant"/>),
/// filtered by the named Tenant filter and by forced row-level security (enabled in the migration).
/// </summary>
public class ShippingDbContext(DbContextOptions<ShippingDbContext> options)
    : AppDbContextBase(options), ITenantDataContext
{
    public DbSet<Shipment> Shipments => Set<Shipment>();
    public DbSet<ShipmentDocument> ShipmentDocuments => Set<ShipmentDocument>();
    public DbSet<DeclarationDraft> DeclarationDrafts => Set<DeclarationDraft>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("shipping");
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        builder.AddTenantOutbox(); // ShipmentReadyForClearanceIntegrationEvent, same transaction

        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();

        // MUST be last: the named tenant filter and the tenant_id indexes of every IMustHaveTenant entity.
        ApplyTenantFilters(builder);
    }
}
