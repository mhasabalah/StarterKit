namespace Agentic.Data.Configurations;

public class AgentRunConfiguration : IEntityTypeConfiguration<AgentRun>
{
    /// <summary>One run per automatic trigger (tenant, agent, trigger key): a redelivered event starts nothing new.</summary>
    public const string TriggerIndexName = "ux_agent_runs_trigger";

    public void Configure(EntityTypeBuilder<AgentRun> builder)
    {
        builder.ToTable("agent_runs");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.AgentKey).IsRequired().HasMaxLength(AgentRun.AgentKeyMaxLength);
        builder.Property(r => r.ModelKey).IsRequired().HasMaxLength(AgentRun.ModelKeyMaxLength);
        builder.Property(r => r.Autonomy).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.Status).IsRequired().HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.TriggerKind).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.TriggerKey).HasMaxLength(AgentRun.TriggerKeyMaxLength);
        builder.Property(r => r.Culture).IsRequired().HasMaxLength(AgentRun.CultureMaxLength);
        builder.Property(r => r.InputJson).HasColumnType("jsonb");
        builder.Property(r => r.OutputJson).HasColumnType("jsonb");
        builder.Property(r => r.EstimatedCost).HasPrecision(18, 6);
        builder.Property(r => r.LeaseOwner).HasMaxLength(AgentRun.LeaseOwnerMaxLength);
        builder.Property(r => r.TraceParent).HasMaxLength(AgentRun.TraceParentMaxLength);
        builder.Property(r => r.FailureCode).HasMaxLength(AgentRun.FailureCodeMaxLength);
        builder.Property(r => r.FailureMessage).HasMaxLength(AgentRun.FailureMessageMaxLength);

        builder.Ignore(r => r.IsTerminal);
        builder.Ignore(r => r.TotalTokens);
        builder.Ignore(r => r.CorrelationId);
        builder.Ignore(r => r.ToolCallBudgetExhausted);

        builder.HasIndex(r => new { r.TenantId, r.AgentKey, r.TriggerKey })
            .IsUnique()
            .HasFilter("trigger_key IS NOT NULL")
            .HasDatabaseName(TriggerIndexName);
        builder.HasIndex(r => new { r.TenantId, r.CreatedAt });
        builder.HasIndex(r => new { r.TenantId, r.InitiatedBy, r.CreatedAt });
        builder.HasIndex(r => new { r.TenantId, r.Status });
    }
}

public class AgentRunStepConfiguration : IEntityTypeConfiguration<AgentRunStep>
{
    public const string SequenceIndexName = "ux_agent_run_steps_sequence";

    public void Configure(EntityTypeBuilder<AgentRunStep> builder)
    {
        builder.ToTable("agent_run_steps");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.Kind).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.Status).IsRequired().HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.ModelId).HasMaxLength(AgentRunStep.ModelIdMaxLength);
        builder.Property(s => s.FinishReason).HasMaxLength(AgentRunStep.FinishReasonMaxLength);
        builder.Property(s => s.CallId).HasMaxLength(AgentRunStep.CallIdMaxLength);
        builder.Property(s => s.ToolName).HasMaxLength(AgentRunStep.ToolNameMaxLength);
        builder.Property(s => s.ErrorCode).HasMaxLength(AgentRunStep.ErrorCodeMaxLength);
        // ArgumentsJson / ResultJson stay text: arguments are untrusted model output and may not be valid JSON.

        builder.Ignore(s => s.IsOpen);

        builder.HasOne<AgentRun>()
            .WithMany()
            .HasForeignKey(s => s.RunId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(s => new { s.TenantId, s.RunId, s.Sequence })
            .IsUnique()
            .HasDatabaseName(SequenceIndexName);
    }
}
