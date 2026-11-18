using Shipping.Domains.Declarations.Models;

namespace Shipping.Data.Configurations;

public class ShipmentConfiguration : IEntityTypeConfiguration<Shipment>
{
    public const string ReferenceIndexName = "ux_shipments_reference";

    public void Configure(EntityTypeBuilder<Shipment> builder)
    {
        builder.ToTable("shipments");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();
        builder.Property(s => s.Reference).IsRequired().HasMaxLength(Shipment.ReferenceMaxLength);
        builder.Property(s => s.OriginCountry).IsRequired().HasMaxLength(2).IsFixedLength();
        builder.Property(s => s.DestinationCountry).IsRequired().HasMaxLength(2).IsFixedLength();
        builder.Property(s => s.HsCode).IsRequired().HasMaxLength(Shipment.HsCodeMaxLength);
        builder.Property(s => s.DeclaredValue).HasPrecision(18, 2);
        builder.Property(s => s.Currency).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(s => s.WeightKg).HasPrecision(12, 3);
        builder.Property(s => s.Status).IsRequired().HasConversion<string>().HasMaxLength(30);

        builder.HasMany(s => s.Documents)
            .WithOne()
            .HasForeignKey(d => d.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Documents).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(s => new { s.TenantId, s.Reference }).IsUnique().HasDatabaseName(ReferenceIndexName);
        builder.HasIndex(s => new { s.TenantId, s.CreatedAt });
    }
}

public class ShipmentDocumentConfiguration : IEntityTypeConfiguration<ShipmentDocument>
{
    public void Configure(EntityTypeBuilder<ShipmentDocument> builder)
    {
        builder.ToTable("shipment_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.Kind).IsRequired().HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.Reference).IsRequired().HasMaxLength(ShipmentDocument.ReferenceMaxLength);
        builder.Property(d => d.Content).HasMaxLength(ShipmentDocument.ContentMaxLength);
        builder.HasIndex(d => new { d.TenantId, d.ShipmentId });
    }
}

public class DeclarationDraftConfiguration : IEntityTypeConfiguration<DeclarationDraft>
{
    /// <summary>One declaration per shipment.</summary>
    public const string ShipmentIndexName = "ux_declaration_drafts_shipment";

    public void Configure(EntityTypeBuilder<DeclarationDraft> builder)
    {
        builder.ToTable("declaration_drafts");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.HsCode).IsRequired().HasMaxLength(Shipment.HsCodeMaxLength);
        builder.Property(d => d.CustomsValue).HasPrecision(18, 2);
        builder.Property(d => d.Duty).HasPrecision(18, 2);
        builder.Property(d => d.Vat).HasPrecision(18, 2);
        builder.Property(d => d.Currency).IsRequired().HasMaxLength(3).IsFixedLength();
        builder.Property(d => d.Notes).HasMaxLength(DeclarationDraft.NotesMaxLength);
        builder.Property(d => d.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.SubmissionReference).HasMaxLength(DeclarationDraft.SubmissionReferenceMaxLength);

        builder.HasOne<Shipment>()
            .WithMany()
            .HasForeignKey(d => d.ShipmentId)
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(d => new { d.TenantId, d.ShipmentId }).IsUnique().HasDatabaseName(ShipmentIndexName);
    }
}
