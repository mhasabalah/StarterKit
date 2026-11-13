using Shared.Core.Messaging.Outbox;

namespace Agentic.Data;

/// <summary>
/// DATA PLANE context (<see cref="ITenantDataContext"/>): every row is tenant-owned (<see cref="IMustHaveTenant"/>),
/// filtered by the named Tenant filter and by forced row-level security (enabled in the migrations). Agent runs, their
/// transcript and everything an agent was told or produced never cross a tenant boundary.
/// </summary>
public class AgenticDbContext(DbContextOptions<AgenticDbContext> options)
    : AppDbContextBase(options), ITenantDataContext
{
    public DbSet<AgentRun> Runs => Set<AgentRun>();
    public DbSet<AgentRunStep> Steps => Set<AgentRunStep>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("agentic");
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        builder.AddTenantOutbox(); // integration events raised by aggregates, same transaction

        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();

        // MUST be last: the named tenant filter and the tenant_id indexes of every IMustHaveTenant entity.
        ApplyTenantFilters(builder);
    }
}
