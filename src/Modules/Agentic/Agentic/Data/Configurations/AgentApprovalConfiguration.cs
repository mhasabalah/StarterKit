namespace Agentic.Data.Configurations;

public class AgentApprovalConfiguration : IEntityTypeConfiguration<AgentApproval>
{
    /// <summary>One approval request per tool-call step.</summary>
    public const string StepIndexName = "ux_agent_approvals_step";

    public void Configure(EntityTypeBuilder<AgentApproval> builder)
    {
        builder.ToTable("agent_approvals");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.AgentKey).IsRequired().HasMaxLength(AgentApproval.AgentKeyMaxLength);
        builder.Property(a => a.ToolName).IsRequired().HasMaxLength(AgentApproval.ToolNameMaxLength);
        builder.Property(a => a.Risk).IsRequired().HasConversion<string>().HasMaxLength(30);
        builder.Property(a => a.RequiredPermission).HasMaxLength(AgentApproval.PermissionMaxLength);
        builder.Property(a => a.ArgumentsJson).IsRequired();
        builder.Property(a => a.PayloadDigest).IsRequired().HasMaxLength(AgentApproval.PayloadDigestLength).IsFixedLength();
        builder.Property(a => a.ModelRationale).HasMaxLength(AgentApproval.RationaleMaxLength);
        builder.Property(a => a.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.DecisionNote).HasMaxLength(AgentApproval.NoteMaxLength);
        builder.Property(a => a.Culture).IsRequired().HasMaxLength(AgentRun.CultureMaxLength);

        builder.HasOne<AgentRun>()
            .WithMany()
            .HasForeignKey(a => a.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => new { a.TenantId, a.StepId })
            .IsUnique()
            .HasDatabaseName(StepIndexName);
        builder.HasIndex(a => new { a.TenantId, a.Status, a.ExpiresAt });
        builder.HasIndex(a => new { a.TenantId, a.RequestedBy, a.RequestedAt });
        builder.HasIndex(a => new { a.TenantId, a.RunId });
    }
}
