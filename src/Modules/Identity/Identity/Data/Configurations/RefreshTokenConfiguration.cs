using Identity.Models;

namespace Identity.Data.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> entity)
    {
        entity.ToTable("refresh_tokens");
        entity.HasKey(e => e.Id);

        entity.HasIndex(e => e.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_refresh_tokens_token_hash");

        entity.HasIndex(e => e.UserId)
            .HasDatabaseName("ix_refresh_tokens_user_id");

        entity.HasIndex(e => e.FamilyId)
            .HasDatabaseName("ix_refresh_tokens_family_id");

        entity.HasIndex(e => e.ExpiresAt)
            .HasDatabaseName("ix_refresh_tokens_expires_at");

        entity.HasIndex(e => new { e.UserId, e.RevokedAt })
            .HasDatabaseName("ix_refresh_tokens_user_id_revoked_at");

        entity.Property(e => e.TokenHash)
            .HasMaxLength(88)
            .IsRequired();

        entity.Property(e => e.SecurityStampHash)
            .HasMaxLength(88)
            .IsRequired();

        entity.Property(e => e.CreatedByIp)
            .HasMaxLength(45)
            .IsRequired();

        entity.Property(e => e.UserAgent)
            .HasMaxLength(512);

        entity.Property(e => e.DeviceName)
            .HasMaxLength(100);

        entity.Property(e => e.RevokedByIp)
            .HasMaxLength(45);

        entity.Property(e => e.RevokedReason)
            .HasMaxLength(200);

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        entity.Ignore(e => e.IsExpired);
        entity.Ignore(e => e.IsRevoked);
        entity.Ignore(e => e.IsActive);
    }
}
