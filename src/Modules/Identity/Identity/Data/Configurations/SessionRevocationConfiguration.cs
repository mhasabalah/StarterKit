using Identity.Domains.Sessions.Models;

namespace Identity.Data.Configurations;

public class SessionRevocationConfiguration : IEntityTypeConfiguration<SessionRevocation>
{
    public void Configure(EntityTypeBuilder<SessionRevocation> builder)
    {
        builder.ToTable("session_revocations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.UserId).IsRequired();
        builder.Property(x => x.AllSessions).IsRequired();
        builder.Property(x => x.RevokedAt).IsRequired();
        builder.HasIndex(x => x.RevokedAt);
    }
}
