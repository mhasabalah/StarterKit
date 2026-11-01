namespace Attachment.Data;

public class AttachmentDbContext(DbContextOptions<AttachmentDbContext> options)
    : AppDbContextBase(options), IDbContext
{
    public DbSet<AttachmentFile> Attachments => Set<AttachmentFile>();
    public DbSet<PendingAttachment> PendingAttachments => Set<PendingAttachment>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.HasDefaultSchema("attachment");
        builder.ApplyConfigurationsFromAssembly(Assembly.GetExecutingAssembly());
        builder.ToSnakeCaseTables();
        builder.FilterSoftDeletedProperties();
        builder.ConfigureVersionConcurrencyTokens();
    }
}
