namespace DataTransfer.Data.Configurations;

public class TransferJobConfiguration : IEntityTypeConfiguration<TransferJob>
{
    /// <summary>One pending/running import per definition per tenant (409 <c>import_already_running</c>).</summary>
    public const string ActiveImportIndexName = "ux_transfer_jobs_active_import";

    public void Configure(EntityTypeBuilder<TransferJob> builder)
    {
        builder.ToTable("transfer_jobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.Id).ValueGeneratedNever();

        builder.Property(j => j.Kind).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.DefinitionKey).IsRequired().HasMaxLength(TransferJob.DefinitionKeyMaxLength);
        builder.Property(j => j.Format).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.Mode).HasConversion<string>().HasMaxLength(20);
        builder.Property(j => j.Policy).HasConversion<string>().HasMaxLength(30);
        builder.Property(j => j.Status).IsRequired().HasConversion<string>().HasMaxLength(30);
        builder.Property(j => j.FileName).HasMaxLength(TransferJob.FileNameMaxLength);
        builder.Property(j => j.FilterJson).HasMaxLength(TransferJob.FilterMaxLength);
        builder.Property(j => j.Culture).IsRequired().HasMaxLength(TransferJob.CultureMaxLength);
        builder.Property(j => j.LeaseOwner).HasMaxLength(TransferJob.LeaseOwnerMaxLength);
        builder.Property(j => j.FailureCode).HasMaxLength(TransferJob.FailureCodeMaxLength);
        builder.Property(j => j.FailureMessage).HasMaxLength(TransferJob.FailureMessageMaxLength);

        builder.HasIndex(j => new { j.TenantId, j.DefinitionKey })
            .IsUnique()
            .HasFilter("kind = 'Import' AND status IN ('Pending', 'Running')")
            .HasDatabaseName(ActiveImportIndexName);
        builder.HasIndex(j => new { j.TenantId, j.CreatedAt });
        builder.HasIndex(j => new { j.TenantId, j.RequestedBy, j.CreatedAt });
        builder.HasIndex(j => new { j.TenantId, j.Status });
        builder.HasIndex(j => new { j.TenantId, j.ExpiresAt });
    }
}

public class TransferJobErrorConfiguration : IEntityTypeConfiguration<TransferJobError>
{
    public void Configure(EntityTypeBuilder<TransferJobError> builder)
    {
        builder.ToTable("transfer_job_errors");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();

        builder.Property(e => e.Column).HasMaxLength(TransferJobError.ColumnMaxLength);
        builder.Property(e => e.Code).IsRequired().HasMaxLength(TransferJobError.CodeMaxLength);
        builder.Property(e => e.Message).IsRequired().HasMaxLength(TransferJobError.MessageMaxLength);

        builder.HasOne<TransferJob>()
            .WithMany()
            .HasForeignKey(e => e.JobId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(e => new { e.TenantId, e.JobId, e.RowNumber });
    }
}
