using Identity.Models;

namespace Identity.Data.Configurations;

public class OtpCodeConfiguration : IEntityTypeConfiguration<OtpCode>
{
    public void Configure(EntityTypeBuilder<OtpCode> builder)
    {
        builder.ToTable("otp_codes");
        builder.HasKey(x => new { x.UserId, x.Purpose });

        builder.Property(x => x.Purpose)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.CodeHash)
            .HasMaxLength(88)
            .IsRequired();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
