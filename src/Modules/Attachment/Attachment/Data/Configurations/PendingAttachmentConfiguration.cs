using Attachment.Domains.Attachments.Models;

namespace Attachment.Data.Configurations;

public class PendingAttachmentConfiguration : IEntityTypeConfiguration<PendingAttachment>
{
    public void Configure(EntityTypeBuilder<PendingAttachment> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.SessionId)
            .IsRequired();

        builder.Property(p => p.FileName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(p => p.OriginalFileName)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(p => p.ContentType)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.FileSize)
            .IsRequired();

        builder.Property(p => p.ContentHash)
            .HasMaxLength(AttachmentFile.ContentHashLength)
            .IsFixedLength();

        builder.Property(p => p.StoragePath)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(p => p.FileUrl)
            .IsRequired()
            .HasMaxLength(1000);

        builder.Property(p => p.ThumbnailPath)
            .HasMaxLength(1000);

        builder.Property(p => p.ThumbnailUrl)
            .HasMaxLength(1000);

        builder.Property(p => p.Type)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(p => p.Category)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(p => p.DisplayOrder)
            .IsRequired();

        builder.Property(p => p.IsPrimary)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(p => p.Width);

        builder.Property(p => p.Height);

        builder.Property(p => p.ExpiresAt)
            .IsRequired();

        builder.Property(p => p.IsAssociated)
            .IsRequired();

        builder.Property(p => p.AssociatedAt);

        // Audit columns (set by AuditableEntityInterceptor)
        builder.Property(p => p.CreatedAt);
        builder.Property(p => p.CreatedBy).HasMaxLength(100);
        builder.Property(p => p.LastModified);
        builder.Property(p => p.LastModifiedBy).HasMaxLength(100);

        // Critical indexes for performance
        builder.HasIndex(p => p.SessionId)
            .HasDatabaseName("IX_PendingAttachments_SessionId");

        builder.HasIndex(p => new { p.ExpiresAt, p.IsAssociated })
            .HasDatabaseName("IX_PendingAttachments_ExpiresAt_IsAssociated");

        // Unique constraint on SessionId + DisplayOrder per tenant (excludes soft-deleted records)
        builder.HasIndex(p => new { p.TenantId, p.SessionId, p.DisplayOrder })
            .IsUnique()
            .HasFilter("is_deleted = false")
            .HasDatabaseName("IX_PendingAttachments_SessionId_DisplayOrder_Unique");

        builder.HasIndex(p => new { p.SessionId, p.IsPrimary })
            .HasDatabaseName("IX_PendingAttachments_SessionId_IsPrimary");

        builder.HasIndex(p => p.IsDeleted);

        builder.HasIndex(p => p.CreatedAt);
    }
}
