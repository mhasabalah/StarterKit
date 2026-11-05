using Webhooks.Domains.Deliveries.Models;
using Webhooks.Domains.Endpoints.Models;

namespace Webhooks.Data.Configurations;

public class WebhookEndpointConfiguration : IEntityTypeConfiguration<WebhookEndpoint>
{
    /// <summary>Explicit name so the repository can recognise the unique violation (-> 409).</summary>
    public const string UrlIndexName = "ux_webhook_endpoints_url";

    public void Configure(EntityTypeBuilder<WebhookEndpoint> builder)
    {
        builder.ToTable("webhook_endpoints");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Url).IsRequired().HasMaxLength(WebhookEndpoint.UrlMaxLength);
        builder.Property(e => e.Description).HasMaxLength(WebhookEndpoint.DescriptionMaxLength);
        builder.Property(e => e.EventTypes).IsRequired().HasColumnType("text[]");
        builder.Property(e => e.SecretProtected).IsRequired().HasMaxLength(1000);
        builder.Property(e => e.Enabled).IsRequired();
        builder.Property(e => e.ConsecutiveFailures).IsRequired();
        builder.Property(e => e.DisabledReason).HasMaxLength(WebhookEndpoint.DisabledReasonMaxLength);

        // One subscription per URL per tenant.
        builder.HasIndex(e => new { e.TenantId, e.Url }).IsUnique().HasDatabaseName(UrlIndexName);
        builder.HasIndex(e => new { e.TenantId, e.Enabled });
    }
}

public class WebhookDeliveryConfiguration : IEntityTypeConfiguration<WebhookDelivery>
{
    /// <summary>Fan-out de-duplication: at-least-once redelivery of the same event creates no second delivery.</summary>
    public const string EventIndexName = "ux_webhook_deliveries_event";

    public void Configure(EntityTypeBuilder<WebhookDelivery> builder)
    {
        builder.ToTable("webhook_deliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.EventType).IsRequired().HasMaxLength(WebhookDelivery.EventTypeMaxLength);
        builder.Property(d => d.Payload).IsRequired().HasColumnType("text");
        builder.Property(d => d.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.LastError).HasMaxLength(WebhookDelivery.ErrorMaxLength);
        builder.Property(d => d.LastResponseSnippet).HasMaxLength(WebhookDelivery.SnippetMaxLength);

        builder.HasOne<WebhookEndpoint>()
            .WithMany()
            .HasForeignKey(d => d.EndpointId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(d => new { d.TenantId, d.EndpointId, d.EventId })
            .IsUnique()
            .HasFilter("redelivery_of_id IS NULL")
            .HasDatabaseName(EventIndexName);
        builder.HasIndex(d => new { d.TenantId, d.Status, d.NextAttemptAt });
        builder.HasIndex(d => new { d.TenantId, d.EndpointId, d.CreatedAt });
        builder.HasIndex(d => new { d.TenantId, d.CreatedAt });
    }
}
