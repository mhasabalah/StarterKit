using Shared.Core.Messaging.Outbox;

namespace DataTransfer.Data;

/// <summary>
/// DATA PLANE context (<see cref="ITenantDataContext"/>): every row is tenant-owned (named <c>Tenant</c> filter + forced
/// RLS). The outbox carries <c>DataImportCompletedIntegrationEvent</c>.
/// </summary>
public class DataTransferDbContext(DbContextOptions<DataTransferDbContext> options)
    : AppDbContextBase(options), ITenantDataContext
{
    public DbSet<TransferJob> Jobs => Set<TransferJob>();
    public DbSet<TransferJobError> Errors => Set<TransferJobError>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("datatransfer");
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        builder.AddTenantOutbox();

        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();

        // Last: tenant filter (named) and tenant_id indexes.
        ApplyTenantFilters(builder);
    }
}
