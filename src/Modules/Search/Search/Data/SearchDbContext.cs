using Search.Domains.Indexes.Models;
using Search.Engine.Postgres;

namespace Search.Data;

/// <summary>
/// DATA PLANE context (<see cref="ITenantDataContext"/>): every row is tenant-owned (<see cref="IMustHaveTenant"/>),
/// filtered by the named Tenant filter and by forced row-level security (enabled in the migration). The index states
/// are here whatever the engine; the documents table is the PostgreSQL engine's store.
/// </summary>
public class SearchDbContext(DbContextOptions<SearchDbContext> options)
    : AppDbContextBase(options), ITenantDataContext
{
    public DbSet<SearchIndexState> IndexStates => Set<SearchIndexState>();

    public DbSet<SearchDocumentEntry> Documents => Set<SearchDocumentEntry>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("search");
        builder.HasPostgresExtension("pg_trgm");
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());

        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();

        // MUST be last: the named tenant filter and the tenant_id indexes of every IMustHaveTenant entity.
        ApplyTenantFilters(builder);
    }
}
