using Search.Domains.Indexes.Models;
using Search.Engine.Postgres;

namespace Search.Data.Configurations;

public sealed class SearchIndexStateConfiguration : IEntityTypeConfiguration<SearchIndexState>
{
    public void Configure(EntityTypeBuilder<SearchIndexState> builder)
    {
        builder.ToTable("search_index_states");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).ValueGeneratedNever();

        builder.Property(s => s.IndexKey).IsRequired().HasMaxLength(SearchIndexState.IndexKeyMaxLength);
        builder.Property(s => s.Status).IsRequired().HasConversion<string>().HasMaxLength(20);
        builder.Property(s => s.DefinitionFingerprint).HasMaxLength(SearchIndexState.FingerprintLength).IsFixedLength();
        builder.Property(s => s.LastErrorCode).HasMaxLength(SearchIndexState.ErrorCodeMaxLength);

        builder.HasIndex(s => new { s.TenantId, s.IndexKey }).IsUnique();
    }
}

public sealed class SearchDocumentEntryConfiguration : IEntityTypeConfiguration<SearchDocumentEntry>
{
    public void Configure(EntityTypeBuilder<SearchDocumentEntry> builder)
    {
        builder.ToTable("search_documents");
        builder.HasKey(d => new { d.TenantId, d.IndexKey, d.DocumentId });

        builder.Property(d => d.IndexKey).HasMaxLength(SearchIndexDefinition.KeyMaxLength);
        builder.Property(d => d.DocumentId).HasMaxLength(SearchDocument.IdMaxLength);
        builder.Property(d => d.SearchVector).IsRequired();
        builder.Property(d => d.SuggestText).IsRequired().HasMaxLength(SearchDocumentEntry.SuggestTextMaxLength);
        builder.Property(d => d.Filters).IsRequired().HasColumnType("jsonb");
        builder.Property(d => d.Content).IsRequired().HasColumnType("jsonb");

        // Full text, typo-tolerant suggestions and containment filters: one GIN index each.
        builder.HasIndex(d => d.SearchVector).HasMethod("gin");
        builder.HasIndex(d => d.SuggestText).HasMethod("gin").HasOperators("gin_trgm_ops");
        builder.HasIndex(d => d.Filters).HasMethod("gin").HasOperators("jsonb_path_ops");
        // The sweep after a rebuild.
        builder.HasIndex(d => new { d.TenantId, d.IndexKey, d.RebuildStamp });
    }
}
