using Agentic.Domains.Knowledge.Models;
using Agentic.Domains.Memory.Models;

namespace Agentic.Data.Configurations;

public class KnowledgeDocumentConfiguration : IEntityTypeConfiguration<KnowledgeDocument>
{
    /// <summary>One document per source and owner id in a tenant: indexing the same document again replaces it.</summary>
    public const string ExternalRefIndexName = "ux_knowledge_documents_external_ref";

    public void Configure(EntityTypeBuilder<KnowledgeDocument> builder)
    {
        builder.ToTable("knowledge_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();

        builder.Property(d => d.SourceKey).IsRequired().HasMaxLength(KnowledgeDocument.SourceKeyMaxLength);
        builder.Property(d => d.ExternalRef).IsRequired().HasMaxLength(KnowledgeDocument.ExternalRefMaxLength);
        builder.Property(d => d.Title).IsRequired().HasMaxLength(KnowledgeDocument.TitleMaxLength);
        builder.Property(d => d.ContentHash).HasMaxLength(KnowledgeDocument.ContentHashLength).IsFixedLength();
        builder.Property(d => d.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.EmbeddingModel).HasMaxLength(KnowledgeDocument.EmbeddingModelMaxLength);
        builder.Property(d => d.FailureCode).HasMaxLength(KnowledgeDocument.FailureCodeMaxLength);

        builder.HasIndex(d => new { d.TenantId, d.SourceKey, d.ExternalRef })
            .IsUnique()
            .HasDatabaseName(ExternalRefIndexName);
        builder.HasIndex(d => new { d.TenantId, d.AttachmentId });
        builder.HasIndex(d => new { d.TenantId, d.Status });
    }
}

public class KnowledgeChunkConfiguration : IEntityTypeConfiguration<KnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<KnowledgeChunk> builder)
    {
        builder.ToTable("knowledge_chunks");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Content).IsRequired();

        builder.HasOne<KnowledgeDocument>()
            .WithMany()
            .HasForeignKey(c => c.DocumentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(c => new { c.TenantId, c.DocumentId, c.Revision, c.Ordinal }).IsUnique();
    }
}

public class AgentMemoryConfiguration : IEntityTypeConfiguration<AgentMemory>
{
    public const string IdempotencyIndexName = "ux_agent_memories_idempotency";

    public void Configure(EntityTypeBuilder<AgentMemory> builder)
    {
        builder.ToTable("agent_memories");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.Scope).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Source).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(m => m.Content).IsRequired().HasMaxLength(AgentMemory.ContentMaxLength);
        builder.Property(m => m.IdempotencyKey).HasMaxLength(AgentMemory.IdempotencyKeyMaxLength);

        builder.HasIndex(m => new { m.TenantId, m.IdempotencyKey })
            .IsUnique()
            .HasFilter("idempotency_key IS NOT NULL")
            .HasDatabaseName(IdempotencyIndexName);
        builder.HasIndex(m => new { m.TenantId, m.Scope, m.OwnerUserId });
        builder.HasIndex(m => new { m.TenantId, m.ExpiresAt });
    }
}
