using Identity.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Identity.Data.Configurations;

/// <summary>
/// Entity Framework configuration for RefreshToken entity
/// </summary>
public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> entity)
    {
        entity.ToTable("refresh_tokens");
        entity.HasKey(e => e.Id);

        // Indexes for performance
        entity.HasIndex(e => e.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_refresh_tokens_token_hash");

        entity.HasIndex(e => e.UserId)
            .HasDatabaseName("ix_refresh_tokens_user_id");

        entity.HasIndex(e => e.ExpiresAt)
            .HasDatabaseName("ix_refresh_tokens_expires_at");

        entity.HasIndex(e => new { e.UserId, e.RevokedAt })
            .HasDatabaseName("ix_refresh_tokens_user_id_revoked_at");

        // Properties
        entity.Property(e => e.TokenHash)
            .HasMaxLength(88)  // Base64 SHA256 hash length
            .IsRequired();

        entity.Property(e => e.CreatedByIp)
            .HasMaxLength(45)  // IPv6 max length
            .IsRequired();

        entity.Property(e => e.RevokedByIp)
            .HasMaxLength(45);

        entity.Property(e => e.RevokedReason)
            .HasMaxLength(200);

        // Relationships
        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Computed properties are not mapped to database
        entity.Ignore(e => e.IsExpired);
        entity.Ignore(e => e.IsRevoked);
        entity.Ignore(e => e.IsActive);
    }
}
