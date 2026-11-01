using Identity.Domains.Onboarding.Models;
using Identity.Domains.Users.Models;

namespace Identity.Data.Configurations;

public class OnboardingProcessConfiguration : IEntityTypeConfiguration<OnboardingProcess>
{
    public void Configure(EntityTypeBuilder<OnboardingProcess> builder)
    {
        builder.ToTable("onboarding_processes");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.UserId)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(x => x.StartedAt)
            .IsRequired();

        builder.Property(x => x.TimeoutHangfireJobId)
            .HasMaxLength(100);

        builder.Property(x => x.ResendCount)
            .HasDefaultValue(0);

        builder.Property(x => x.CreatedAt)
            .IsRequired();

        // One partial unique index enforces "at most one active process per user".
        // Active = Registered(0) | OtpSent(1) | PhoneVerified(2).
        // Typed via enum cast so a reordering of enum values causes a compile error,
        // not a silently broken filter.
        var activeStatusFilter =
            $"\"status\" IN ({(int)OnboardingStatus.Registered}, " +
            $"{(int)OnboardingStatus.OtpSent}, " +
            $"{(int)OnboardingStatus.PhoneVerified})";

        builder.HasIndex(x => x.UserId)
            .IsUnique()
            .HasFilter(activeStatusFilter)
            .HasDatabaseName("ix_onboarding_processes_user_id_active");

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
