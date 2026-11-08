#if (OUTBOX)
using Shared.Core.Messaging.Outbox;
#endif
#if (EXAMPLE)
using ModuleName.Domains.Items.Models;
#endif

namespace ModuleName.Data;

#if (TENANT_DATA)
/// <summary>
/// DATA PLANE context (<see cref="ITenantDataContext"/>): every row is tenant-owned (<see cref="IMustHaveTenant"/>),
/// filtered by the named Tenant filter and by forced row-level security (enabled in the migration).
/// </summary>
public class ModuleNameDbContext(DbContextOptions<ModuleNameDbContext> options)
    : AppDbContextBase(options), ITenantDataContext
#else
/// <summary>CONTROL PLANE context: platform data, not tenant-owned (no tenant filters, no RLS).</summary>
public class ModuleNameDbContext(DbContextOptions<ModuleNameDbContext> options) : AppDbContextBase(options)
#endif
{
#if (EXAMPLE)
    public DbSet<Item> Items => Set<Item>();

#endif
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("modulename");
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

#if (OUTBOX)
#if (TENANT_DATA)
        builder.AddTenantOutbox(); // integration events raised by aggregates, same transaction (ItemCreatedIntegrationEvent)
#else
        builder.AddOutbox(); // integration events raised by aggregates, same transaction (ItemCreatedIntegrationEvent)
#endif

#endif
        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();
#if (TENANT_DATA)

        // MUST be last: the named tenant filter and the tenant_id indexes of every IMustHaveTenant entity.
        ApplyTenantFilters(builder);
#endif
    }
}
