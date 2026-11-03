using Shared.Core.Messaging.Outbox;
using Shared.Core.MultiTenancy;
using Shared.Core.MultiTenancy.Data;

namespace Attachment.Data;

/// <summary>DATA PLANE context (<see cref="ITenantDataContext"/>): attachments belong to a tenant.</summary>
public class AttachmentDbContext(DbContextOptions<AttachmentDbContext> options)
    : AppDbContextBase(options), IDbContext, ITenantDataContext
{
    public DbSet<AttachmentFile> Attachments => Set<AttachmentFile>();
    public DbSet<PendingAttachment> PendingAttachments => Set<PendingAttachment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("attachment");
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        // Transactional outbox (UserAvatarChangedIntegrationEvent & co); tenant-owned row: filter + RLS like the rest.
        builder.AddTenantOutbox();
        builder.ToSnakeCaseTables();
        builder.FilterSoftDeletedProperties();

        // Every read sees only Ready, not-deleted attachments (Uploading / PendingDeletion rows are invisible):
        // the named Lifecycle filter is AND-ed with the generic SoftDelete filter. Maintenance code (upload phase 2
        // works on the tracked entity; the deletion job and the sweeper) uses IgnoreNonIsolationFilters().
        builder.Entity<AttachmentFile>()
            .HasQueryFilter(QueryFilterNames.Lifecycle, a => a.Status == AttachmentStatus.Ready);

        builder.ConfigureVersionConcurrencyTokens();

        // Last: tenant filter (named, AND-ed with the others) and tenant_id indexes.
        ApplyTenantFilters(builder);
    }
}
