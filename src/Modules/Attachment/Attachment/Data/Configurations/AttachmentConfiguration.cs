namespace Attachment.Data.Configurations;

public class AttachmentConfiguration : IEntityTypeConfiguration<AttachmentFile>
{
    public void Configure(EntityTypeBuilder<AttachmentFile> builder)
    {
        builder.HasKey(a => a.Id);

        builder.Property(a => a.FileName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(a => a.OriginalFileName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(a => a.ContentType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.FileSize)
            .IsRequired();

        builder.Property(a => a.StoragePath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(a => a.FileUrl)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(a => a.ThumbnailPath)
            .HasMaxLength(1000);

        builder.Property(a => a.ThumbnailUrl)
            .HasMaxLength(1000);

        builder.Property(a => a.Type)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(a => a.Category)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(a => a.EntityType)
            .HasConversion<string>();

        builder.Property(a => a.EntityId);

        builder.Property(a => a.UploadedBy)
            .IsRequired();

        builder.Property(a => a.IsPublic)
            .IsRequired();

        builder.Property(a => a.Width);

        builder.Property(a => a.Height);

        builder.Property(a => a.Description)
            .HasMaxLength(1000);

        builder.Property(a => a.ContentHash)
            .HasMaxLength(AttachmentFile.ContentHashLength)
            .IsFixedLength();

        // Lifecycle. The DB default only matters for rows that existed before the column (they are Ready);
        // the code always writes the status explicitly (the enum has no 0 member, so EF never skips it).
        builder.Property(a => a.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(32)
            .HasDefaultValue(AttachmentStatus.Ready);

        builder.Property(a => a.DeletionRequestedAt);

        builder.Property(a => a.DeletionAttempts)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(a => a.NextDeletionAttemptAt);

        // Audit columns (set by AuditableEntityInterceptor)
        builder.Property(a => a.CreatedAt);
        builder.Property(a => a.CreatedBy).HasMaxLength(100);
        builder.Property(a => a.LastModified);
        builder.Property(a => a.LastModifiedBy).HasMaxLength(100);

        // Indexes
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => new { a.EntityType, a.EntityId, a.Type });
        builder.HasIndex(a => a.UploadedBy);
        builder.HasIndex(a => a.Category);
        builder.HasIndex(a => a.Type);
        builder.HasIndex(a => a.IsDeleted);
        builder.HasIndex(a => a.CreatedAt);
        builder.HasIndex(a => a.ContentHash);

        // Sweeper lookups: abandoned uploads (status + created_at) and due deletions (status + next attempt).
        builder.HasIndex(a => new { a.Status, a.CreatedAt });
        builder.HasIndex(a => new { a.Status, a.NextDeletionAttemptAt });

        // The query filter (Ready + not deleted) is set in AttachmentDbContext.OnModelCreating, AFTER the generic
        // soft-delete filter, which would otherwise replace it.
    }
}
