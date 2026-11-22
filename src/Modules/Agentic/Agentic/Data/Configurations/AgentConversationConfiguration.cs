namespace Agentic.Data.Configurations;

public class AgentConversationConfiguration : IEntityTypeConfiguration<AgentConversation>
{
    public void Configure(EntityTypeBuilder<AgentConversation> builder)
    {
        builder.ToTable("agent_conversations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();

        builder.Property(c => c.AgentKey).IsRequired().HasMaxLength(AgentRun.AgentKeyMaxLength);
        builder.Property(c => c.Title).HasMaxLength(AgentConversation.TitleMaxLength);
        builder.Property(c => c.Summary).HasMaxLength(AgentConversation.SummaryMaxLength);
        builder.Property(c => c.SummaryCost).HasPrecision(18, 6);
        builder.Property(c => c.Status).IsRequired().HasConversion<string>().HasMaxLength(20);

        // "My conversations, most recent first".
        builder.HasIndex(c => new { c.TenantId, c.OwnerId, c.LastMessageAt });
    }
}

public class AgentModelUsageConfiguration : IEntityTypeConfiguration<AgentModelUsage>
{
    public void Configure(EntityTypeBuilder<AgentModelUsage> builder)
    {
        builder.ToTable("agent_model_usages");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.Kind).IsRequired().HasConversion<string>().HasMaxLength(40);
        builder.Property(u => u.AgentKey).IsRequired().HasMaxLength(AgentRun.AgentKeyMaxLength);
        builder.Property(u => u.Model).IsRequired().HasMaxLength(AgentModelUsage.ModelMaxLength);
        builder.Property(u => u.EstimatedCost).HasPrecision(18, 6);

        // GET /agent-usage reads a period.
        builder.HasIndex(u => new { u.TenantId, u.CreatedAt });
    }
}
