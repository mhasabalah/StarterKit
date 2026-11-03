namespace Identity.Data.Configurations;

public class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        // Phone numbers are stored normalized to E.164 (see IPhoneNumberNormalizer), so the stored value IS the
        // normalized value. One account per phone number: a reset OTP can never reach the wrong user,
        // and phone lookups (login, forgot password) use the index instead of a full scan.
        builder.HasIndex(u => u.PhoneNumber)
            .IsUnique()
            .HasFilter("\"phone_number\" IS NOT NULL")
            .HasDatabaseName("ix_users_phone_number");
    }
}
