namespace Tenancy.Data;

/// <summary>
/// CONTROL PLANE context (always the host database, never a tenant's dedicated database): tenants, branches,
/// memberships, tenant roles, invitations and the tenant audit trail. Tenant-owned rows still get the named
/// <c>Tenant</c> filter, so tenant-facing handlers only ever see the current tenant's rows; infrastructure lookups
/// (tenant store, membership resolver, tenant creation) run in an explicit host scope.
/// </summary>
public class TenancyDbContext(DbContextOptions<TenancyDbContext> options) : AppDbContextBase(options), IDbContext
{
    public const string Schema = "tenancy";

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<TenantMembership> Memberships => Set<TenantMembership>();
    public DbSet<TenantRole> Roles => Set<TenantRole>();
    public DbSet<TenantInvitation> Invitations => Set<TenantInvitation>();
    public DbSet<TenantAuditLog> AuditLogs => Set<TenantAuditLog>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureAuditLogIsAppendOnly();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        EnsureAuditLogIsAppendOnly();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema(Schema);
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        builder.ToSnakeCaseTables();
        builder.ConfigureVersionConcurrencyTokens();

        // Last: Tenant filter on tenant-owned rows (branches, memberships, roles, invitations, audit) + indexes.
        ApplyTenantFilters(builder);
    }

    private void EnsureAuditLogIsAppendOnly()
    {
        if (TenantContext.IsHostScope) return; // the tenant purge removes a deleted tenant's trail

        if (ChangeTracker.Entries<TenantAuditLog>().Any(e => e.State is EntityState.Modified or EntityState.Deleted))
        {
            throw new InvalidOperationException("The tenant audit log is append-only.");
        }
    }
}
