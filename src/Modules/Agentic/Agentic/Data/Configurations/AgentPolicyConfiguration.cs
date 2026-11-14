namespace Agentic.Data.Configurations;

public class AgentPolicyConfiguration : IEntityTypeConfiguration<AgentPolicy>
{
    /// <summary>One policy per tenant and agent.</summary>
    public const string AgentIndexName = "ux_agent_policies_agent";

    public void Configure(EntityTypeBuilder<AgentPolicy> builder)
    {
        builder.ToTable("agent_policies");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.AgentKey).IsRequired().HasMaxLength(AgentRun.AgentKeyMaxLength);
        builder.Property(p => p.AutonomyCeiling).HasConversion<string>().HasMaxLength(20);

        builder.Ignore(p => p.Settings);

        builder.HasIndex(p => new { p.TenantId, p.AgentKey })
            .IsUnique()
            .HasDatabaseName(AgentIndexName);
    }
}
