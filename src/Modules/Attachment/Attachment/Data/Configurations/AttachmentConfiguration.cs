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
    }
}
