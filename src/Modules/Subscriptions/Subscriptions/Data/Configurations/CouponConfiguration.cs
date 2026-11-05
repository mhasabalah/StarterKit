using Subscriptions.Domains.Coupons.Models;

namespace Subscriptions.Data.Configurations;

public class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public const string CodeIndex = "ux_coupons_code";

    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("coupons");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.Code).IsRequired().HasMaxLength(Coupon.MaxCodeLength);
        builder.Property(x => x.Description).HasMaxLength(500);
        builder.Property(x => x.DiscountType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.Value).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired(false);
        builder.Property(x => x.Duration).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(x => x.DurationPeriods).IsRequired(false);
        builder.Property(x => x.MaxRedemptions).IsRequired(false);
        builder.Property(x => x.RedemptionCount).IsRequired();
        builder.Property(x => x.ExpiresAt).IsRequired(false);
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.AllowedPlanIds).HasColumnType("uuid[]").IsRequired();
        builder.Property(x => x.UpdatedAt).IsRequired();

        builder.HasIndex(x => x.Code).IsUnique().HasDatabaseName(CodeIndex);
    }
}

public class CouponRedemptionConfiguration : IEntityTypeConfiguration<CouponRedemption>
{
    public const string OnePerSubscriberIndex = "ux_coupon_redemptions_coupon_subscriber";

    public void Configure(EntityTypeBuilder<CouponRedemption> builder)
    {
        builder.ToTable("coupon_redemptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.CouponId).IsRequired();
        builder.Property(x => x.SubscriberId).IsRequired();
        builder.Property(x => x.SubscriptionId).IsRequired();
        builder.Property(x => x.RedeemedAt).IsRequired();

        builder.HasOne<Coupon>().WithMany().HasForeignKey(x => x.CouponId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.CouponId, x.SubscriberId }).IsUnique().HasDatabaseName(OnePerSubscriberIndex);
        builder.HasIndex(x => x.SubscriptionId);
    }
}
