using Identity.Models;

namespace Identity.Data.Configurations;

public class ApiKeyConfiguration : IEntityTypeConfiguration<ApiKey>
{
    public void Configure(EntityTypeBuilder<ApiKey> entity)
    {
        entity.ToTable("api_keys");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Id).ValueGeneratedNever();

        entity.HasIndex(e => e.Prefix).IsUnique().HasDatabaseName("ix_api_keys_prefix");
        entity.HasIndex(e => new { e.UserId, e.RevokedAt }).HasDatabaseName("ix_api_keys_user_id_revoked_at");

        entity.Property(e => e.Name).HasMaxLength(ApiKey.MaxNameLength).IsRequired();
        entity.Property(e => e.Prefix).HasMaxLength(ApiKey.PrefixLength).IsRequired();
        entity.Property(e => e.KeyHash).HasMaxLength(88).IsRequired();

        // text[] in Postgres; compared as a set, so no value comparer ordering concerns.
        entity.Property(e => e.Scopes).HasColumnType("text[]").IsRequired();

        entity.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
